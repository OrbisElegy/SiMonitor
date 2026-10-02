/* SPDX-License-Identifier: AGPL-3.0-or-later */
#include "sim_audio.h"
#define MINIAUDIO_IMPLEMENTATION
#define MA_ENABLE_ONLY_SPECIFIC_BACKENDS
#ifdef SIM_AUDIO_TEST
#define MA_ENABLE_NULL
#else
#define MA_ENABLE_WASAPI
#endif
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#define MA_NO_GENERATION
#include "vendor/miniaudio.h"
#include <math.h>
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
#include <audioclient.h>
/* Local IID avoids an extra import-library dependency. */
static const GUID sa_iid_audio_clock = {0xcd63314f, 0x3fba, 0x4a1b,
    {0x81, 0x2c, 0xef, 0x96, 0x35, 0x87, 0x28, 0xe7}};
#endif

struct sa_output {
    ma_context context;
    ma_device device;
    ma_pcm_rb ring;
    ma_uint32 retired;
    ma_uint32 missing;
    /* Immutable open-time diagnostic snapshot, never queried in callback. */
    ma_uint32 period_status, period_hr;
    ma_uint32 default_period, fundamental_period, minimum_period, maximum_period;
    ma_uint32 current_period, engine_rate, engine_channels;
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    IAudioClock* clock;
    HRESULT clock_hr;
#endif
};

static void snapshot_periods(sa_output* output)
{
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    ma_IAudioClient3* client = NULL;
    MA_WAVEFORMATEX* format = NULL;
    ma_uint32 current = 0, normal = 0, fundamental = 0, minimum = 0, maximum = 0;
    HRESULT hr;
    output->period_status = 2;
    hr = ma_IAudioClient_QueryInterface((ma_IAudioClient*)output->device.wasapi.pAudioClientPlayback,
        &MA_IID_IAudioClient3, (void**)&client);
    if (SUCCEEDED(hr)) {
        hr = ma_IAudioClient3_GetCurrentSharedModeEnginePeriod(client, &format, &current);
        if (SUCCEEDED(hr) && format == NULL) hr = E_POINTER;
        if (SUCCEEDED(hr) && format != NULL) {
            hr = ma_IAudioClient3_GetSharedModeEnginePeriod(client, format,
                &normal, &fundamental, &minimum, &maximum);
            if (SUCCEEDED(hr)) {
                output->default_period = normal; output->fundamental_period = fundamental;
                output->minimum_period = minimum; output->maximum_period = maximum;
                output->current_period = current;
                output->engine_rate = format->nSamplesPerSec;
                output->engine_channels = format->nChannels;
                output->period_status = 1;
            }
        }
        if (format != NULL) ma_CoTaskMemFree((&output->context), format);
        ma_IAudioClient3_Release(client);
    }
    output->period_hr = (ma_uint32)hr;
#else
    (void)output; /* Unsupported/test builds never synthesize Windows periods. */
#endif
}

static void consume(sa_output* output, float* pcm, ma_uint32 frames)
{
    ma_uint32 done = 0;
    memset(pcm, 0, (size_t)frames * sizeof(float));
    if (ma_atomic_load_32(&output->retired)) return;
    while (done < frames) {
        void* p = NULL;
        ma_uint32 count = frames - done;
        if (ma_pcm_rb_acquire_read(&output->ring, &count, &p) != MA_SUCCESS || count == 0) break;
        memcpy(pcm + done, p, (size_t)count * sizeof(float));
        ma_pcm_rb_commit_read(&output->ring, count);
        done += count;
    }
    if (done != frames) {
        ma_atomic_store_32(&output->missing, frames - done);
#if defined(SIM_AUDIO_TEST) && defined(SIM_AUDIO_TEST_BEFORE_UNDERRUN)
        SIM_AUDIO_TEST_BEFORE_UNDERRUN(output);
#endif
        /* A concurrent stop/reroute notification must keep its retirement reason. */
        ma_atomic_compare_and_swap_32(&output->retired, 0, 1);
    }
}
static void data_callback(ma_device* device, void* output, const void* input, ma_uint32 frames)
{
    (void)input;
    consume((sa_output*)device->pUserData, (float*)output, frames);
}
static void notification_callback(const ma_device_notification* notification)
{
    if (notification->type == ma_device_notification_type_stopped ||
        notification->type == ma_device_notification_type_rerouted ||
        notification->type == ma_device_notification_type_interruption_began) {
        sa_output* output = (sa_output*)notification->pDevice->pUserData;
        ma_atomic_exchange_32(&output->retired, 2);
    }
}
uint32_t sa_abi_version(void) { return 1; }
int32_t sa_open(const char* device_id_utf8, uint32_t capacity_ms, sa_output** out)
{
    sa_output* output;
    ma_backend backend;
    ma_device_config config;
#ifdef _WIN32
    ma_device_id selected;
#endif
    if (!out) return -1;
    *out = NULL;
    if (capacity_ms < 20 || capacity_ms > 100 || (device_id_utf8 && !device_id_utf8[0])) return -1;
#ifdef SIM_AUDIO_TEST
    if (device_id_utf8) return -2;
    backend = ma_backend_null;
#elif defined(_WIN32)
    backend = ma_backend_wasapi;
#else
    (void)device_id_utf8;
    return -2; /* Never silently fall back to null/another OS backend. */
#endif
    output = (sa_output*)calloc(1, sizeof(*output));
    if (!output) return -2;
    if (ma_context_init(&backend, 1, NULL, &output->context) != MA_SUCCESS) { free(output); return -2; }
    if (ma_pcm_rb_init(ma_format_f32, 1, 48 * capacity_ms, NULL, NULL, &output->ring) != MA_SUCCESS) {
        ma_context_uninit(&output->context); free(output); return -2;
    }
    config = ma_device_config_init(ma_device_type_playback);
    config.playback.format = ma_format_f32;
    config.playback.channels = 1;
    config.playback.shareMode = ma_share_mode_shared;
    config.sampleRate = 48000;
    config.performanceProfile = ma_performance_profile_low_latency;
    config.wasapi.noAutoConvertSRC = MA_TRUE;
    config.dataCallback = data_callback;
    config.notificationCallback = notification_callback;
    config.pUserData = output;
#ifdef _WIN32
    if (device_id_utf8) {
        memset(&selected, 0, sizeof(selected));
        if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, device_id_utf8, -1,
                (LPWSTR)selected.wasapi, 64)) {
            ma_pcm_rb_uninit(&output->ring); ma_context_uninit(&output->context); free(output); return -1;
        }
        config.playback.pDeviceID = &selected;
    }
#endif
    if (ma_device_init(&output->context, &config, &output->device) != MA_SUCCESS) {
        ma_pcm_rb_uninit(&output->ring); ma_context_uninit(&output->context); free(output); return -2;
    }
    snapshot_periods(output);
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    /* Own a reference for this stream generation, not a reroutable raw client. */
    output->clock_hr = ma_IAudioClient_GetService((ma_IAudioClient*)output->device.wasapi.pAudioClientPlayback,
        &sa_iid_audio_clock, (void**)&output->clock);
#endif
    *out = output;
    return 0;
}
int32_t sa_submit(sa_output* output, const float* pcm, uint32_t frames)
{
    ma_uint32 done = 0, i;
    if (!output || (!pcm && frames)) return -1;
    if (ma_atomic_load_32(&output->retired)) return -4;
    if (frames > ma_pcm_rb_available_write(&output->ring)) return -3;
    for (i = 0; i < frames; i++) if (!isfinite(pcm[i]) || fabsf(pcm[i]) > 1) return -1;
    while (done < frames) {
        void* p = NULL;
        ma_uint32 count = frames - done;
        ma_pcm_rb_acquire_write(&output->ring, &count, &p);
        memcpy(p, pcm + done, (size_t)count * sizeof(float));
        ma_pcm_rb_commit_write(&output->ring, count);
        done += count;
    }
    return ma_atomic_load_32(&output->retired) ? -4 : 0;
}
int32_t sa_start(sa_output* output)
{
    if (!output) return -1;
    if (ma_atomic_load_32(&output->retired)) return -4;
    return ma_device_start(&output->device) == MA_SUCCESS ? 0 : -2;
}
int32_t sa_close(sa_output* output)
{
    if (!output) return -1;
    ma_atomic_store_32(&output->retired, 2);
    if (ma_device_is_started(&output->device) && ma_device_stop(&output->device) != MA_SUCCESS) return -2;
    ma_device_uninit(&output->device); /* Stops/joins device worker before freeing PCM. */
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    if (output->clock) output->clock->lpVtbl->Release(output->clock);
#endif
    ma_pcm_rb_uninit(&output->ring);
    ma_context_uninit(&output->context);
    free(output);
    return 0;
}
uint32_t sa_info(sa_output* output, uint32_t key)
{
    if (!output) return 0;
    switch (key) {
        case 1: return output->device.playback.internalSampleRate;
        case 2: return output->device.playback.internalChannels;
        case 3:
            switch (output->device.playback.internalFormat) {
                case ma_format_f32: return 1;
                case ma_format_s16: return 2;
                case ma_format_s24: return 3;
                case ma_format_s32: return 4;
                case ma_format_u8: return 5;
                default: return 0;
            }
        case 4: return output->device.playback.internalPeriodSizeInFrames;
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
        case 5: return output->device.wasapi.actualBufferSizeInFramesPlayback;
#endif
        case 6: return ma_atomic_load_32(&output->retired);
        case 7: return ma_atomic_load_32(&output->missing);
        case 8: return ma_pcm_rb_available_write(&output->ring); /* Producer only. */
        case 10: return output->period_status;
        case 11: return output->default_period;
        case 12: return output->fundamental_period;
        case 13: return output->minimum_period;
        case 14: return output->maximum_period;
        case 15: return output->current_period;
        case 16: return output->engine_rate;
        case 17: return output->period_hr;
        case 18: return output->engine_channels;
        default: return 0;
    }
}
int32_t sa_clock_sample(sa_output* output, uint64_t* position,
    uint64_t* frequency, uint64_t* qpc_100ns, uint32_t* hresult)
{
    if (!position || !frequency || !qpc_100ns || !hresult) return -1;
    *position = 0; *frequency = 0; *qpc_100ns = 0; *hresult = 0;
    if (!output) return -1;
    if (ma_atomic_load_32(&output->retired)) return -4;
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    {
        IAudioClock* clock = output->clock;
        UINT64 p = 0, f = 0, q = 0;
        HRESULT hr = output->clock_hr;
        if (SUCCEEDED(hr) && clock != NULL) {
            hr = clock->lpVtbl->GetFrequency(clock, &f);
            if (hr == S_OK) hr = clock->lpVtbl->GetPosition(clock, &p, &q);
            if (SUCCEEDED(hr) && f != 0) {
                *position = p; *frequency = f; *qpc_100ns = q;
                *hresult = (uint32_t)hr;
                return hr == S_OK ? 0 : 1;
            }
        }
        if (SUCCEEDED(hr)) hr = E_UNEXPECTED;
        *hresult = (uint32_t)hr;
    }
#endif
    return -2;
}
#ifdef SIM_AUDIO_TEST
void sa_test_render(sa_output* output, float* pcm, uint32_t frames) { consume(output, pcm, frames); }
#endif

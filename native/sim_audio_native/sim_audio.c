/* SPDX-License-Identifier: AGPL-3.0-only */
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
};

static void snapshot_periods(sa_output* s)
{
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
    ma_IAudioClient3* client = NULL;
    MA_WAVEFORMATEX* format = NULL;
    ma_uint32 current = 0, normal = 0, fundamental = 0, minimum = 0, maximum = 0;
    HRESULT hr;
    s->period_status = 2;
    hr = ma_IAudioClient_QueryInterface((ma_IAudioClient*)s->device.wasapi.pAudioClientPlayback,
        &MA_IID_IAudioClient3, (void**)&client);
    if (SUCCEEDED(hr)) {
        hr = ma_IAudioClient3_GetCurrentSharedModeEnginePeriod(client, &format, &current);
        if (SUCCEEDED(hr) && format == NULL) hr = E_POINTER;
        if (SUCCEEDED(hr) && format != NULL) {
            hr = ma_IAudioClient3_GetSharedModeEnginePeriod(client, format,
                &normal, &fundamental, &minimum, &maximum);
            if (SUCCEEDED(hr)) {
                s->default_period = normal; s->fundamental_period = fundamental;
                s->minimum_period = minimum; s->maximum_period = maximum;
                s->current_period = current;
                s->engine_rate = format->nSamplesPerSec;
                s->engine_channels = format->nChannels;
                s->period_status = 1;
            }
        }
        if (format != NULL) ma_CoTaskMemFree((&s->context), format);
        ma_IAudioClient3_Release(client);
    }
    s->period_hr = (ma_uint32)hr;
#else
    (void)s; /* Unsupported/test builds never synthesize Windows periods. */
#endif
}

static void consume(sa_output* s, float* pcm, ma_uint32 frames)
{
    ma_uint32 done = 0;
    memset(pcm, 0, (size_t)frames * sizeof(float));
    if (ma_atomic_load_32(&s->retired)) return;
    while (done < frames) {
        void* p = NULL;
        ma_uint32 count = frames - done;
        if (ma_pcm_rb_acquire_read(&s->ring, &count, &p) != MA_SUCCESS || count == 0) break;
        memcpy(pcm + done, p, (size_t)count * sizeof(float));
        ma_pcm_rb_commit_read(&s->ring, count);
        done += count;
    }
    if (done != frames) {
        ma_atomic_store_32(&s->missing, frames - done);
        ma_atomic_store_32(&s->retired, 1);
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
        sa_output* s = (sa_output*)notification->pDevice->pUserData;
        ma_atomic_exchange_32(&s->retired, 2);
    }
}
uint32_t sa_abi_version(void) { return 1; }
int32_t sa_open(const char* id, uint32_t capacity_ms, sa_output** out)
{
    sa_output* s;
    ma_backend backend;
    ma_device_config config;
#ifdef _WIN32
    ma_device_id selected;
#endif
    if (!out) return -1;
    *out = NULL;
    if (capacity_ms < 20 || capacity_ms > 100 || (id && !id[0])) return -1;
#ifdef SIM_AUDIO_TEST
    if (id) return -2;
    backend = ma_backend_null;
#elif defined(_WIN32)
    backend = ma_backend_wasapi;
#else
    (void)id;
    return -2; /* Never silently fall back to null/another OS backend. */
#endif
    s = (sa_output*)calloc(1, sizeof(*s));
    if (!s) return -2;
    if (ma_context_init(&backend, 1, NULL, &s->context) != MA_SUCCESS) { free(s); return -2; }
    if (ma_pcm_rb_init(ma_format_f32, 1, 48 * capacity_ms, NULL, NULL, &s->ring) != MA_SUCCESS) {
        ma_context_uninit(&s->context); free(s); return -2;
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
    config.pUserData = s;
#ifdef _WIN32
    if (id) {
        memset(&selected, 0, sizeof(selected));
        if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, id, -1,
                (LPWSTR)selected.wasapi, 64)) {
            ma_pcm_rb_uninit(&s->ring); ma_context_uninit(&s->context); free(s); return -1;
        }
        config.playback.pDeviceID = &selected;
    }
#endif
    if (ma_device_init(&s->context, &config, &s->device) != MA_SUCCESS) {
        ma_pcm_rb_uninit(&s->ring); ma_context_uninit(&s->context); free(s); return -2;
    }
    snapshot_periods(s);
    *out = s;
    return 0;
}
int32_t sa_submit(sa_output* s, const float* pcm, uint32_t frames)
{
    ma_uint32 done = 0, i;
    if (!s || (!pcm && frames)) return -1;
    if (ma_atomic_load_32(&s->retired)) return -4;
    if (frames > ma_pcm_rb_available_write(&s->ring)) return -3;
    for (i = 0; i < frames; i++) if (!isfinite(pcm[i]) || fabsf(pcm[i]) > 1) return -1;
    while (done < frames) {
        void* p = NULL;
        ma_uint32 count = frames - done;
        ma_pcm_rb_acquire_write(&s->ring, &count, &p);
        memcpy(p, pcm + done, (size_t)count * sizeof(float));
        ma_pcm_rb_commit_write(&s->ring, count);
        done += count;
    }
    return ma_atomic_load_32(&s->retired) ? -4 : 0;
}
int32_t sa_start(sa_output* s)
{
    if (!s) return -1;
    if (ma_atomic_load_32(&s->retired)) return -4;
    return ma_device_start(&s->device) == MA_SUCCESS ? 0 : -2;
}
int32_t sa_close(sa_output* s)
{
    if (!s) return -1;
    ma_atomic_store_32(&s->retired, 2);
    if (ma_device_is_started(&s->device) && ma_device_stop(&s->device) != MA_SUCCESS) return -2;
    ma_device_uninit(&s->device); /* Stops/joins device worker before freeing PCM. */
    ma_pcm_rb_uninit(&s->ring);
    ma_context_uninit(&s->context);
    free(s);
    return 0;
}
uint32_t sa_info(sa_output* s, uint32_t key)
{
    if (!s) return 0;
    switch (key) {
        case 1: return s->device.playback.internalSampleRate;
        case 2: return s->device.playback.internalChannels;
        case 3:
            switch (s->device.playback.internalFormat) {
                case ma_format_f32: return 1;
                case ma_format_s16: return 2;
                case ma_format_s24: return 3;
                case ma_format_s32: return 4;
                case ma_format_u8: return 5;
                default: return 0;
            }
        case 4: return s->device.playback.internalPeriodSizeInFrames;
#if defined(_WIN32) && !defined(SIM_AUDIO_TEST)
        case 5: return s->device.wasapi.actualBufferSizeInFramesPlayback;
#endif
        case 6: return ma_atomic_load_32(&s->retired);
        case 7: return ma_atomic_load_32(&s->missing);
        case 8: return ma_pcm_rb_available_write(&s->ring); /* Producer only. */
        case 10: return s->period_status;
        case 11: return s->default_period;
        case 12: return s->fundamental_period;
        case 13: return s->minimum_period;
        case 14: return s->maximum_period;
        case 15: return s->current_period;
        case 16: return s->engine_rate;
        case 17: return s->period_hr;
        case 18: return s->engine_channels;
        default: return 0;
    }
}
#ifdef SIM_AUDIO_TEST
void sa_test_render(sa_output* s, float* pcm, uint32_t frames) { consume(s, pcm, frames); }
#endif

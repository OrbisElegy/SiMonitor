/* SPDX-License-Identifier: AGPL-3.0-only */
#ifndef SIM_AUDIO_H
#define SIM_AUDIO_H
#include <stdint.h>
#ifdef _WIN32
#define SA_API __declspec(dllexport)
#else
#define SA_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct sa_output sa_output;
/* ABI1: opaque handle; 48kHz mono float32 PCM. One producer, one native
   consumer; control calls serialized with producer. Close only on owner thread.
   Results: 0 success, -1 invalid, -2 unavailable, -3 full, -4 retired.
   No handles or buffers may be used after successful close. */
SA_API uint32_t sa_abi_version(void);
SA_API int32_t sa_open(const char* device_id_utf8, uint32_t capacity_ms, sa_output** out);
SA_API int32_t sa_submit(sa_output* out, const float* pcm, uint32_t frames);
SA_API int32_t sa_start(sa_output* out);
SA_API int32_t sa_close(sa_output* out);
/* Scalar keys:1 native rate,2 channels,3 format(1 f32,2 s16,3 s24,4 s32,5 u8),
   4 reported period frames(NOT latency),5 actual WASAPI buffer frames,
   6 retired reason(0 none,1 underrun,2 device change/stop),7 missing frames,
   8 writable engine frames,9 low-latency qualified(always0 until measured).
   Optional ABI1 extension:10 period snapshot status(0 unavailable/older DLL,
   1 available,2 query failed),11 default,12 fundamental,13 minimum,14 maximum,
   15 current engine period,16 engine rate,17 HRESULT bits,18 engine channels.
   11-18 are an open-time snapshot, NOT live values or physical latency. */
SA_API uint32_t sa_info(sa_output* out, uint32_t key);
/* Optional ABI1 extension. Owner thread only, never device callback.
   Returns0 accurate,1 reduced accuracy(S_FALSE),-2 unavailable,-4 retired.
   Device position units MUST be divided by frequency; QPC is already100ns,
   not raw QueryPerformanceCounter ticks. Outputs zeroed on failure. */
SA_API int32_t sa_clock_sample(sa_output* out, uint64_t* position,
    uint64_t* frequency, uint64_t* qpc_100ns, uint32_t* hresult);
#ifdef SIM_AUDIO_TEST
/* Test-only build: no hardware. Runs the SAME PCM consumer as the callback. */
SA_API void sa_test_render(sa_output* out, float* pcm, uint32_t frames);
#endif
#ifdef __cplusplus
}
#endif
#endif

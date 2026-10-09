/* SPDX-License-Identifier: AGPL-3.0-or-later */
#ifndef SIM_AUDIO_TEST
#error This regression requires the isolated test backend.
#endif
#include "../sim_audio.c"
#include <stdio.h>

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); \
        exit(EXIT_FAILURE); \
    } \
} while (0)

/* Drive miniaudio's real callback dispatcher, not just sa_test_render.
   The stopped null device has no competing worker. A WASAPI startup burst
   can request several periods before the managed producer runs again. */
static void check_callback_burst(uint32_t frames_per_request)
{
    sa_output* output = NULL;
    float source[1920];
    float pcm[1920];
    uint32_t offset, index;
    CHECK(sa_open(NULL, 40, &output) == 0);
    for (index = 0; index < 1920; index++) {
        source[index] = (float)((int)(index % 99) - 49) / 50;
    }
    CHECK(sa_submit(output, source, 1920) == 0);
    for (offset = 0; offset < 1920; offset += frames_per_request) {
        uint32_t frames = ma_min(frames_per_request, 1920 - offset);
        ma_device__on_data(&output->device, pcm, NULL, frames);
        CHECK(memcmp(pcm, source + offset, frames * sizeof(float)) == 0);
        CHECK(sa_info(output, 6) == 0);
        CHECK(sa_info(output, 7) == 0);
        CHECK(sa_info(output, 8) == offset + frames);
    }
    /* Exact exhaustion is healthy. Only an actual extra request underruns. */
    ma_device__on_data(&output->device, pcm, NULL, 1);
    CHECK(pcm[0] == 0);
    CHECK(sa_info(output, 6) == 1 && sa_info(output, 7) == 1);
    CHECK(sa_close(output) == 0);
}

int main(void)
{
    check_callback_burst(480);
    check_callback_burst(1920);
    check_callback_burst(127);
    puts("PASS callback bursts consume only requested PCM without eager prefetch");
    return EXIT_SUCCESS;
}

/* SPDX-License-Identifier: AGPL-3.0-or-later */
#ifndef SIM_AUDIO_TEST
#error This regression requires the isolated test backend.
#endif
#include "../sim_audio.h"
#include <stdio.h>
#include <stdlib.h>

/* Compile the real consumer and notification callback into this test only.
   The hook is absent from both the production and ordinary test libraries. */
static void before_underrun_retirement(sa_output* output);
#define SIM_AUDIO_TEST_BEFORE_UNDERRUN before_underrun_retirement
#include "../sim_audio.c"

static sa_output* expected_output;
static ma_device_notification_type notification_type;
static int notify_before_underrun;
static unsigned int hook_calls;

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); \
        exit(EXIT_FAILURE); \
    } \
} while (0)

static void notify(sa_output* output, ma_device_notification_type type)
{
    ma_device_notification notification;
    memset(&notification, 0, sizeof(notification));
    notification.pDevice = &output->device;
    notification.type = type;
    notification_callback(&notification);
}

static void before_underrun_retirement(sa_output* output)
{
    CHECK(output == expected_output);
    CHECK(sa_info(output, 6) == 0);
    CHECK(sa_info(output, 7) == 2);
    hook_calls += 1;
    if (notify_before_underrun) {
        notify(output, notification_type);
        CHECK(sa_info(output, 6) == 2);
    }
}

static void prepare_output(sa_output* output)
{
    const float source[] = {0.25f, -0.5f};
    memset(output, 0, sizeof(*output));
    /* No device worker: the two callback orders are controlled synchronously. */
    output->device.pUserData = output;
    CHECK(ma_pcm_rb_init(ma_format_f32, 1, 8, NULL, NULL, &output->ring) == MA_SUCCESS);
    CHECK(sa_submit(output, source, 2) == 0);
    expected_output = output;
    notify_before_underrun = 0;
    hook_calls = 0;
}

static void check_partial_render(sa_output* output)
{
    float pcm[] = {1, 1, 1, 1};
    sa_test_render(output, pcm, 4);
    CHECK(pcm[0] == 0.25f && pcm[1] == -0.5f && pcm[2] == 0 && pcm[3] == 0);
    CHECK(sa_info(output, 7) == 2);
    CHECK(hook_calls == 1);
}

static void check_retired_silence(sa_output* output, ma_uint32 reason)
{
    const float source[] = {0.25f};
    float pcm[] = {1, 1, 1, 1};
    unsigned int previous_hook_calls = hook_calls;
    CHECK(sa_submit(output, source, 1) == -4);
    sa_test_render(output, pcm, 4);
    CHECK(pcm[0] == 0 && pcm[1] == 0 && pcm[2] == 0 && pcm[3] == 0);
    CHECK(sa_info(output, 6) == reason);
    CHECK(hook_calls == previous_hook_calls);
}

static void check_underrun(void)
{
    sa_output output;
    prepare_output(&output);
    check_partial_render(&output);
    CHECK(sa_info(&output, 6) == 1);
    check_retired_silence(&output, 1);
    CHECK(sa_info(&output, 7) == 2);
    ma_pcm_rb_uninit(&output.ring);
    puts("PASS ordinary underrun retires output and preserves silence");
}

static void check_notification_order(ma_device_notification_type type, int notification_first)
{
    sa_output output;
    prepare_output(&output);
    notification_type = type;
    notify_before_underrun = notification_first;
    check_partial_render(&output);
    if (!notification_first) {
        CHECK(sa_info(&output, 6) == 1);
        notify(&output, type);
    }
    CHECK(sa_info(&output, 6) == 2);
    check_retired_silence(&output, 2);
    CHECK(sa_info(&output, 7) == 2);
    ma_pcm_rb_uninit(&output.ring);
    printf("PASS notification %d %s underrun retirement keeps reason 2\n",
        (int)type, notification_first ? "before" : "after");
}

static void check_already_retired(ma_device_notification_type type)
{
    sa_output output;
    prepare_output(&output);
    notify(&output, type);
    check_retired_silence(&output, 2);
    CHECK(sa_info(&output, 7) == 0);
    CHECK(hook_calls == 0);
    CHECK(ma_pcm_rb_available_read(&output.ring) == 2);
    ma_pcm_rb_uninit(&output.ring);
}

static void check_consumer_wakeup(void)
{
    sa_output output;
    float pcm[] = {1, 1};
    prepare_output(&output);
    CHECK(sa_wait_writable(&output, 1) == -2);
    CHECK(consumed_init(&output));
    CHECK(sa_wait_writable(NULL, 1) == -1);
    CHECK(sa_wait_writable(&output, 0) == -1);
    CHECK(sa_wait_writable(&output, 1001) == -1);
    CHECK(sa_wait_writable(&output, 1) == 1);
    /* A consumer pass before the wait is latched, then consumed exactly once. */
    sa_test_render(&output, pcm, 2);
    CHECK(sa_info(&output, 6) == 0 && hook_calls == 0);
    CHECK(sa_wait_writable(&output, 1000) == 0);
    CHECK(sa_wait_writable(&output, 1) == 1);
    /* Underrun retirement wakes the producer and reports retirement. */
    sa_test_render(&output, pcm, 2);
    CHECK(sa_info(&output, 6) == 1 && hook_calls == 1);
    CHECK(sa_wait_writable(&output, 1000) == -4);
    consumed_uninit(&output);
    ma_pcm_rb_uninit(&output.ring);

    prepare_output(&output);
    CHECK(consumed_init(&output));
    notify(&output, ma_device_notification_type_stopped);
    CHECK(sa_wait_writable(&output, 1000) == -4);
    consumed_uninit(&output);
    ma_pcm_rb_uninit(&output.ring);
    puts("PASS consumer progress and retirement wake the producer once");
}

int main(void)
{
    const ma_device_notification_type types[] = {
        ma_device_notification_type_stopped,
        ma_device_notification_type_rerouted,
        ma_device_notification_type_interruption_began
    };
    size_t index;
    check_underrun();
    for (index = 0; index < sizeof(types) / sizeof(types[0]); index++) {
        check_notification_order(types[index], 1);
        check_notification_order(types[index], 0);
        check_already_retired(types[index]);
    }
    puts("PASS retirement interleavings and already-retired output");
    check_consumer_wakeup();
    return EXIT_SUCCESS;
}

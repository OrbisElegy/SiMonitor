#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Exercise ABI and the actual native consumer; never claim hardware evidence."""
import ctypes as c
import math
from pathlib import Path
import sys
import time


def check(condition, reason):
    if not condition:
        raise AssertionError(reason)


def main():
    lib = c.CDLL(str(Path(sys.argv[1]).resolve()))
    lib.sa_abi_version.restype = c.c_uint32
    lib.sa_open.argtypes = [c.c_char_p, c.c_uint32, c.POINTER(c.c_void_p)]
    lib.sa_submit.argtypes = [c.c_void_p, c.POINTER(c.c_float), c.c_uint32]
    lib.sa_start.argtypes = [c.c_void_p]
    lib.sa_close.argtypes = [c.c_void_p]
    lib.sa_info.argtypes = [c.c_void_p, c.c_uint32]
    lib.sa_info.restype = c.c_uint32
    lib.sa_clock_sample.argtypes = [c.c_void_p, c.POINTER(c.c_uint64), c.POINTER(c.c_uint64), c.POINTER(c.c_uint64), c.POINTER(c.c_uint32)]
    check(lib.sa_abi_version() == 1, 'ABI version must be 1')
    h = c.c_void_p()
    check(lib.sa_open(None, 19, c.byref(h)) == -1 and not h.value, 'Invalid buffer size must be rejected without a handle')
    if '--production-unavailable' in sys.argv:
        check(lib.sa_open(None, 40, c.byref(h)) == -2 and not h.value, 'Unsupported backend must be rejected without a handle')
        check(not hasattr(lib, 'sa_test_render'), 'Production library must not export the test sink')
        print('PASS production Linux build rejects unsupported backend; no test sink exported')
        sys.exit(0)
    lib.sa_test_render.argtypes = [c.c_void_p, c.POINTER(c.c_float), c.c_uint32]
    check(lib.sa_open(None, 40, c.byref(h)) == 0, 'A fresh test output must open after retirement')
    try:
        check(lib.sa_info(h, 8) == 1920 and lib.sa_info(h, 9) == 0, 'Fresh ring must expose its capacity and no submitted frames')
        check(all(lib.sa_info(h, key) == 0 for key in range(10, 19)), 'Test backend must not report hardware diagnostics')
        position, frequency, qpc, hr = c.c_uint64(9), c.c_uint64(9), c.c_uint64(9), c.c_uint32(9)
        check(lib.sa_clock_sample(h, c.byref(position), c.byref(frequency), c.byref(qpc), c.byref(hr)) == -2, 'Test backend must report an unavailable device clock')
        check((position.value, frequency.value, qpc.value, hr.value) == (0, 0, 0, 0), 'Unavailable clock values must be cleared')
        source = (c.c_float * 1500)(*[(i % 99 - 49) / 50 for i in range(1500)])
        check(lib.sa_submit(h, source, 1500) == 0, 'Initial PCM submission must succeed')
        check(lib.sa_submit(h, source, 1500) == -3, 'PCM exceeding ring capacity must be rejected')
        first = (c.c_float * 1000)()
        lib.sa_test_render(h, first, 1000)
        check(list(first) == list(source)[:1000], 'Consumer must render submitted PCM in order')
        invalid = (c.c_float * 2)(0, math.nan)
        check(lib.sa_submit(h, invalid, 2) == -1, 'Non-finite PCM must be rejected')
        check(lib.sa_submit(h, source, 1000) == 0, 'PCM submission after consumption must succeed')
        tail = (c.c_float * 2000)()
        lib.sa_test_render(h, tail, 2000)
        check(list(tail) == list(source)[1000:] + list(source)[:1000] + [0] * 500, 'Wrapped PCM must be followed by underrun silence')
        check(lib.sa_info(h, 6) == 1 and lib.sa_info(h, 7) == 500, 'Underrun must retire output and record missing frames')
        check(lib.sa_submit(h, source, 1) == -4 and lib.sa_start(h) == -4, 'Retired output must reject submission and start')
        check(lib.sa_clock_sample(h, c.byref(position), c.byref(frequency), c.byref(qpc), c.byref(hr)) == -4, 'Retired output must reject clock sampling')
        lib.sa_test_render(h, tail, 2000)
        check(not any(tail), 'Retired output must render silence')
    finally:
        close_status = lib.sa_close(h)
        check(close_status == 0, 'Native ABI contract must hold: lib.sa_close(h) == 0')
    # Exercise real miniaudio null-worker start/stop/join, not just manual reads.
    check(lib.sa_open(None, 40, c.byref(h)) == 0, 'A fresh test output must open after retirement')
    try:
        check(lib.sa_info(h, 6) == 0, 'A fresh output must not be retired')
        silence = (c.c_float * 1920)()
        check(lib.sa_submit(h, silence, 1920) == 0, 'A fresh output must accept PCM')
        check(lib.sa_start(h) == 0, 'Native worker must start')
        deadline = time.monotonic() + 3
        while lib.sa_info(h, 6) == 0 and time.monotonic() < deadline:
            time.sleep(0.005)
        check(lib.sa_info(h, 6) == 1 and lib.sa_info(h, 7) > 0, 'Native worker must report an underrun')
    finally:
        close_status = lib.sa_close(h)
        check(close_status == 0, 'Native ABI contract must hold: lib.sa_close(h) == 0')
    print('PASS native ABI, ring wrap, rejection, underrun fencing, fresh handle and worker join')


if __name__ == '__main__':
    main()

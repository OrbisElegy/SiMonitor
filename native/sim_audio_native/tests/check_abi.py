#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Exercise ABI and the actual native consumer; never claim hardware evidence."""
import ctypes as c
import math
from pathlib import Path
import sys
import time

lib = c.CDLL(str(Path(sys.argv[1]).resolve()))
lib.sa_abi_version.restype = c.c_uint32
lib.sa_open.argtypes = [c.c_char_p, c.c_uint32, c.POINTER(c.c_void_p)]
lib.sa_submit.argtypes = [c.c_void_p, c.POINTER(c.c_float), c.c_uint32]
lib.sa_start.argtypes = [c.c_void_p]
lib.sa_close.argtypes = [c.c_void_p]
lib.sa_info.argtypes = [c.c_void_p, c.c_uint32]
lib.sa_info.restype = c.c_uint32
lib.sa_clock_sample.argtypes = [c.c_void_p, c.POINTER(c.c_uint64), c.POINTER(c.c_uint64), c.POINTER(c.c_uint64), c.POINTER(c.c_uint32)]
assert lib.sa_abi_version() == 1
h = c.c_void_p()
assert lib.sa_open(None, 19, c.byref(h)) == -1 and not h.value
if '--production-unavailable' in sys.argv:
    assert lib.sa_open(None, 40, c.byref(h)) == -2 and not h.value
    assert not hasattr(lib, 'sa_test_render')
    print('PASS production Linux build rejects unsupported backend; no test sink exported')
    sys.exit(0)
lib.sa_test_render.argtypes = [c.c_void_p, c.POINTER(c.c_float), c.c_uint32]
assert lib.sa_open(None, 40, c.byref(h)) == 0
try:
    assert lib.sa_info(h, 8) == 1920 and lib.sa_info(h, 9) == 0
    assert all(lib.sa_info(h, key) == 0 for key in range(10, 19))
    position, frequency, qpc, hr = c.c_uint64(9), c.c_uint64(9), c.c_uint64(9), c.c_uint32(9)
    assert lib.sa_clock_sample(h, c.byref(position), c.byref(frequency), c.byref(qpc), c.byref(hr)) == -2
    assert (position.value, frequency.value, qpc.value, hr.value) == (0, 0, 0, 0)
    source = (c.c_float * 1500)(*[(i % 99 - 49) / 50 for i in range(1500)])
    assert lib.sa_submit(h, source, 1500) == 0
    assert lib.sa_submit(h, source, 1500) == -3
    first = (c.c_float * 1000)()
    lib.sa_test_render(h, first, 1000)
    assert list(first) == list(source)[:1000]
    invalid = (c.c_float * 2)(0, math.nan)
    assert lib.sa_submit(h, invalid, 2) == -1
    assert lib.sa_submit(h, source, 1000) == 0
    tail = (c.c_float * 2000)()
    lib.sa_test_render(h, tail, 2000)
    assert list(tail) == list(source)[1000:] + list(source)[:1000] + [0] * 500
    assert lib.sa_info(h, 6) == 1 and lib.sa_info(h, 7) == 500
    assert lib.sa_submit(h, source, 1) == -4 and lib.sa_start(h) == -4
    assert lib.sa_clock_sample(h, c.byref(position), c.byref(frequency), c.byref(qpc), c.byref(hr)) == -4
    lib.sa_test_render(h, tail, 2000)
    assert not any(tail)
finally:
    assert lib.sa_close(h) == 0
# Exercise real miniaudio null-worker start/stop/join, not just manual reads.
assert lib.sa_open(None, 40, c.byref(h)) == 0
try:
    assert lib.sa_info(h, 6) == 0
    silence = (c.c_float * 1920)()
    assert lib.sa_submit(h, silence, 1920) == 0
    assert lib.sa_start(h) == 0
    deadline = time.monotonic() + 3
    while lib.sa_info(h, 6) == 0 and time.monotonic() < deadline:
        time.sleep(0.005)
    assert lib.sa_info(h, 6) == 1 and lib.sa_info(h, 7) > 0
finally:
    assert lib.sa_close(h) == 0
print('PASS native ABI, ring wrap, rejection, underrun fencing, fresh handle and worker join')

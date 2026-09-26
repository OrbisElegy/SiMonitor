# SPDX-License-Identifier: AGPL-3.0-or-later
"""Keep native ABI checks behind the command-line entry point."""
import contextlib
import io
from pathlib import Path
import runpy
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


SCRIPT = Path(__file__).resolve().parents[1] / 'native/sim_audio_native/tests/check_abi.py'


def library_fixture():
    return SimpleNamespace(
        sa_abi_version=Mock(return_value=1),
        sa_open=Mock(side_effect=[-1, -2]),
        sa_submit=Mock(),
        sa_start=Mock(),
        sa_close=Mock(return_value=0),
        sa_info=Mock(return_value=0),
        sa_clock_sample=Mock(),
        sa_wait_writable=Mock(return_value=-1),
    )


class NativeAudioAbiEntryPointTests(unittest.TestCase):
    def run_cli(self, library, optimize, production_unavailable=True):
        binary = SCRIPT.with_name('production-library-fixture')
        arguments = [str(SCRIPT), str(binary)]
        if production_unavailable:
            arguments.append('--production-unavailable')
        code = compile(SCRIPT.read_text(encoding='utf-8'), str(SCRIPT), 'exec', optimize=optimize)
        with patch('sys.argv', arguments), patch('ctypes.CDLL', return_value=library) as load_library:
            try:
                exec(code, {'__name__': '__main__'})
            finally:
                load_library.assert_called_once_with(str(binary.resolve()))

    def test_import_does_not_read_cli_arguments_or_load_native_library(self):
        with patch('sys.argv', [str(SCRIPT)]), patch('ctypes.CDLL') as load_library:
            module = runpy.run_path(str(SCRIPT), run_name='native_audio_check_abi')
        load_library.assert_not_called()
        self.assertTrue(callable(module['main']))

    def test_production_unavailable_cli_keeps_success_exit_and_diagnostics(self):
        for optimize in (0, 1, 2):
            with self.subTest(optimize=optimize):
                library = library_fixture()
                output = io.StringIO()
                with contextlib.redirect_stdout(output), self.assertRaises(SystemExit) as exit_status:
                    self.run_cli(library, optimize)
                self.assertEqual(exit_status.exception.code, 0)
                library.sa_abi_version.assert_called_once_with()
                self.assertEqual(library.sa_open.call_count, 2)
                self.assertEqual(output.getvalue(),
                                 'PASS production Linux build rejects unsupported backend; no test sink exported\n')

    def test_invalid_production_library_is_rejected_at_every_optimization_level(self):
        for optimize in (0, 1, 2):
            for defect, reason in (('abi_version', 'ABI version'),
                                   ('backend', 'Unsupported backend'),
                                   ('test_sink', 'test sink')):
                with self.subTest(optimize=optimize, defect=defect):
                    library = library_fixture()
                    if defect == 'abi_version':
                        library.sa_abi_version.return_value = 99
                    elif defect == 'backend':
                        library.sa_open.side_effect = [-1, 0]
                    else:
                        library.sa_test_render = Mock()
                    output = io.StringIO()
                    with contextlib.redirect_stdout(output), self.assertRaisesRegex(AssertionError, reason):
                        self.run_cli(library, optimize)
                    self.assertEqual(output.getvalue(), '')

    def test_failed_native_check_closes_the_open_output_at_every_optimization_level(self):
        for optimize in (0, 1, 2):
            with self.subTest(optimize=optimize):
                library = library_fixture()
                library.sa_test_render = Mock()
                library.sa_open.side_effect = [-1, 0]
                with self.assertRaisesRegex(AssertionError, 'Fresh ring'):
                    self.run_cli(library, optimize, production_unavailable=False)
                library.sa_close.assert_called_once()


if __name__ == '__main__':
    unittest.main()

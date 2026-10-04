#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline checks of oxygen amounts, time boundaries and adult parameter input."""
from dataclasses import replace
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from oxygenation_model import (
    AdultProfile, BTPS_TO_STPD, Control, Parameters, Phase, State, StateRates, advance,
    derivative, equilibrate, oxygen_content_ml_per_ml, pressure_from_content,
    saturation_fraction, simulate, steady_state, ventilation_ml_stpd_per_s,
    with_blood_volume,
)
from run_oxygenation_prototype import ROOT, configured_parameters, crossing


class AdultParameterTests(unittest.TestCase):
    def test_nadler_units_and_independent_coefficient_examples(self):
        # Hand-calculated from the published coefficients (1.70 m, 70 kg).
        self.assertAlmostEqual(AdultProfile(35, 'male', 170, 70).blood_volume_ml(), 4659.9797, places=7)
        self.assertAlmostEqual(AdultProfile(35, 'female', 170, 70).blood_volume_ml(), 4248.4193, places=7)
        self.assertEqual(AdultProfile(18, 'male', 170, 70).blood_volume_ml(),
                         AdultProfile(80, 'male', 170, 70).blood_volume_ml())

    def test_non_adult_and_outside_declared_estimator_scope_are_rejected(self):
        for profile in ((17, 'male', 170, 70), (35, 'unknown', 170, 70),
                        (35, 'female', 160, 110), (35, 'male', 200, 60),
                        (float('nan'), 'male', 170, 70)):
            with self.subTest(profile=profile), self.assertRaises(ValueError):
                AdultProfile(*profile)

    def test_explicit_volume_and_split_are_independent_of_demographics(self):
        for total in (2500, 3500, 6500, 7500):
            for fraction in (0.1, 0.2, 0.3):
                p = with_blood_volume(Parameters(), total, fraction)
                self.assertAlmostEqual(p.arterial_volume_ml + p.venous_volume_ml, total)
                self.assertAlmostEqual(p.arterial_volume_ml / total, fraction)
                self.assertEqual(p.oxygen_demand_ml_stpd_per_min, 250)
        with self.assertRaises(ValueError):
            with_blood_volume(Parameters(), 200)

    def test_configuration_records_estimated_and_overridden_total(self):
        config = json.loads((ROOT / 'eng/physiology/oxygenation-prototype.json').read_text(encoding='utf-8'))
        estimated, _ = configured_parameters(config)
        self.assertAlmostEqual(estimated.arterial_volume_ml + estimated.venous_volume_ml, 4823.7546875)
        config['blood_volume'] = {'method': 'explicit', 'total_volume_ml': 6500, 'arterial_fraction': 0.3}
        explicit, _ = configured_parameters(config)
        self.assertEqual(explicit.arterial_volume_ml, 1950)
        self.assertEqual(explicit.venous_volume_ml, 4550)

    def test_invalid_physiology_is_rejected_before_simulation(self):
        for changes in ({'cardiac_output_ml_per_min': 0}, {'frc_ml_btps': float('nan')},
                        {'hemoglobin_g_per_dl': True}, {'shunt_fraction': -0.01}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                replace(Parameters(), **changes)
        with self.assertRaises(ValueError):
            Control(airway_open=False)
        with self.assertRaises(ValueError):
            Phase(0.0000015, Control())


class OxygenBalanceTests(unittest.TestCase):
    def setUp(self):
        self.parameters = Parameters()
        self.control = Control()
        self.initial = steady_state(self.parameters, self.control)

    def test_dissociation_anchors_and_inverse_content(self):
        for pressure, expected in ((100, 97.7465), (60, 90.5797), (40, 74.9465)):
            self.assertAlmostEqual(100 * saturation_fraction(pressure), expected, delta=0.001)
        for hb in (6, 15, 20):
            for pressure in (0, 20, 40, 60, 100, 500, 1000):
                content = oxygen_content_ml_per_ml(pressure, hb)
                self.assertAlmostEqual(pressure_from_content(content, hb), pressure, delta=1e-9)
        self.assertGreater(oxygen_content_ml_per_ml(100, 15), 1.34 * 15 * saturation_fraction(100) / 100)

    def test_btps_conversion_and_dead_space_remove_delivered_volume(self):
        self.assertAlmostEqual(BTPS_TO_STPD, 0.826238365, delta=1e-9)
        p, c = self.parameters, self.control
        inspiration = ventilation_ml_stpd_per_s(p, c, 0, True)
        self.assertAlmostEqual(inspiration * p.inspiration_us / 1e6, 300 * BTPS_TO_STPD)
        self.assertEqual(ventilation_ml_stpd_per_s(p, c, p.inspiration_us, True), 0)
        self.assertEqual(ventilation_ml_stpd_per_s(p, replace(c, tidal_volume_ml_btps=150), 0, True), 0)

    def test_analytic_steady_state_satisfies_all_five_storage_equations(self):
        alveolar_ventilation_ml_stpd_per_s = ventilation_ml_stpd_per_s(self.parameters, self.control, 0, False)
        rates = derivative(self.initial, self.parameters, self.control, alveolar_ventilation_ml_stpd_per_s)
        self.assertIsInstance(rates, StateRates)
        for rate in rates[:5]:
            self.assertAlmostEqual(rate, 0, delta=1e-12)
        self.assertAlmostEqual(rates.consumed_oxygen_ml_stpd_per_s, 250 / 60)

    def test_closed_apnea_consumes_oxygen_without_external_flow(self):
        closed = Control(tidal_volume_ml_btps=0, airway_open=False)
        samples = simulate(self.parameters, [Phase(60, closed)], self.initial)
        final = samples[-1].state
        self.assertAlmostEqual(self.initial.oxygen_inventory_ml - final.oxygen_inventory_ml, 250, delta=1e-8)
        self.assertEqual(final.inspired_oxygen_ml, 0)
        self.assertEqual(final.expired_oxygen_ml, 0)
        self.assertAlmostEqual(final.consumed_oxygen_ml, 250, delta=1e-9)
        self.assertLess(samples[-1].arterial_saturation_percent, samples[0].arterial_saturation_percent)
        self.assertLess(max(abs(s.oxygen_balance_error_ml) for s in samples), 1e-8)

    def test_zero_demand_closed_equilibrium_does_not_artificially_desaturate(self):
        p = replace(self.parameters, oxygen_demand_ml_stpd_per_min=0)
        initial = steady_state(p, self.control)
        final = simulate(p, [Phase(30, Control(tidal_volume_ml_btps=0, airway_open=False))], initial)[-1].state
        for before, after in zip(initial, final, strict=True):
            self.assertAlmostEqual(before, after, delta=1e-10)

    def test_limited_consumption_and_signed_lung_exchange_preserve_mass(self):
        state = State(1, 100, 1700, 50, 40)
        closed = Control(tidal_volume_ml_btps=0, airway_open=False)
        rate = derivative(state, self.parameters, closed, 0)
        self.assertGreater(rate.alveolar_oxygen_ml_stpd_per_s, 0)  # Venous blood gives oxygen back to gas.
        self.assertGreater(rate.unmet_oxygen_demand_ml_stpd_per_s, 0)
        self.assertAlmostEqual(rate.consumed_oxygen_ml_stpd_per_s, 250 / 60 / 2)
        self.assertAlmostEqual(rate.oxygen_inventory_ml_stpd_per_s + rate.consumed_oxygen_ml_stpd_per_s, 0, delta=1e-12)
        self.assertAlmostEqual(rate.consumed_oxygen_ml_stpd_per_s + rate.unmet_oxygen_demand_ml_stpd_per_s, 250 / 60)

    def test_checkpoint_roundtrip_preserves_absolute_breath_phase_and_ledger(self):
        # The checkpoint lies inside expiration, on the integration grid.
        first = advance(self.initial, self.parameters, self.control, 0, 2_150_000, 50_000)
        restored = State(*json.loads(json.dumps(first)))
        resumed = advance(restored, self.parameters, self.control, 2_150_000, 7_500_000, 50_000)
        whole = advance(self.initial, self.parameters, self.control, 0, 7_500_000, 50_000)
        self.assertEqual(whole, resumed)

    def test_breath_and_phase_boundaries_are_split_with_irregular_step(self):
        p = replace(self.parameters, respiratory_rate_per_min=17, inspiration_fraction=0.37)
        whole = advance(self.initial, p, self.control, 0, p.period_us, 47_000)
        first = advance(self.initial, p, self.control, 0, p.inspiration_us, 47_000)
        resumed = advance(first, p, self.control, p.inspiration_us, p.period_us, 47_000)
        self.assertEqual(whole, resumed)
        rows = simulate(p, [Phase(0.317, self.control), Phase(0.619, Control(tidal_volume_ml_btps=0, airway_open=False))],
                        self.initial, step_seconds=0.047)
        self.assertEqual([s.time_seconds for s in rows], [0, 0.317, 0.5, 0.936])
        self.assertEqual(rows[1].state.inspired_oxygen_ml, rows[-1].state.inspired_oxygen_ml)

    def test_rejected_state_and_step_leave_input_unchanged(self):
        for invalid in (self.initial._replace(alveolar_oxygen_ml=-1),
                        self.initial._replace(venous_oxygen_ml=float('nan'))):
            with self.assertRaises(ValueError):
                simulate(self.parameters, [Phase(1, self.control)], invalid)
        before = tuple(self.initial)
        with self.assertRaises(ValueError):
            simulate(self.parameters, [Phase(1, self.control)], self.initial, step_seconds=0.1)
        self.assertEqual(tuple(self.initial), before)

    def test_periodic_initialization_and_censored_crossings(self):
        initial, cycles = equilibrate(self.parameters, self.control)
        self.assertGreater(cycles, 0)
        rows = simulate(self.parameters, [Phase(120, self.control)], initial)
        self.assertIsNone(crossing(rows, 90))
        self.assertEqual(crossing(rows, 100), 0)
        self.assertLess(abs(rows[0].arterial_saturation_percent - rows[-1].arterial_saturation_percent), 1e-7)


class ToolContractTests(unittest.TestCase):
    def test_import_does_not_write_spawn_or_connect(self):
        script = '''
import os, sys
sys.path.insert(0, sys.argv[1])
def audit(event, args):
    if event.startswith(('subprocess.', 'socket.')) or event in ('os.system', 'os.mkdir', 'os.remove', 'os.rename'):
        raise AssertionError(event)
    if event == 'open' and args[2] & (os.O_WRONLY | os.O_RDWR | os.O_CREAT | os.O_TRUNC):
        raise AssertionError('write on import')
sys.addaudithook(audit)
import oxygenation_model, run_oxygenation_prototype, replay_oxygenation_transport
'''
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, '-B', '-c', script, str(ROOT / 'tools')], cwd=directory,
                                    capture_output=True, text=True, encoding='utf-8', check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, '')
            self.assertEqual(list(Path(directory).iterdir()), [])

    def test_help_and_bad_demographics_do_not_create_results(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'output'
            command = [sys.executable, str(ROOT / 'tools/run_oxygenation_prototype.py'), '--output', str(output)]
            for arguments, expected in ((['--help'], 0), (['--age-years', '35'], 2),
                (['--age-years', '35', '--sex', 'male', '--height-cm', '175', '--weight-kg', '70', '--blood-volume-ml', '5000'], 2),
                (['--step-ms', 'nan'], 2), (['--blood-volume-ml', '200'], 1)):
                with self.subTest(arguments=arguments):
                    result = subprocess.run(command + arguments, capture_output=True, text=True, encoding='utf-8', check=False)
                    self.assertEqual(result.returncode, expected, result.stderr)
                    self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()

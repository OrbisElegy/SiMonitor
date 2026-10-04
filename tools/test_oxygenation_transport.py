#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Checks for physical-input replay and its original-time exchange contract."""
import copy
import json
import unittest

from oxygenation_model import Control, Parameters, derivative, steady_state
from replay_oxygenation_transport import ROOT, replay


class TransportReplayTests(unittest.TestCase):
    def setUp(self):
        self.config = json.loads((ROOT / 'eng/physiology/oxygenation-prototype.json').read_text(encoding='utf-8'))
        self.trace = {
            'Format': 'OxygenTransportReplayInput@1', 'SampleStepNs': 8_000_000,
            'Transport': {
                'ModelId': 'PhysiologyTransportInputs@1',
                'Physiology': {'EpochAnchorSimTimeNs': 0, 'ConductionPattern': 0, 'SeededRate': None,
                    'CardiacActivity': 0, 'VentricularMechanicalEnabled': True, 'MechanicalAfterCycles': None,
                    'MechanicalDurationCycles': None, 'MechanicalEveryCycles': 1, 'VentricularConductionRatio': 1,
                    'ConductedBeatsPerGroup': 1, 'IndependentVentricularPeriodNs': None, 'HeartPeriodNs': 800_000_000,
                    'BreathPeriodNs': 3_750_000_000, 'InspirationDurationNs': 1_875_000_000, 'InspiratoryPauseNs': 0},
                'Ventilation': {'TidalVolumeMicrolitersBtps': 450_000, 'DeadSpaceMicrolitersBtps': 150_000,
                    'InspiredOxygenMillionths': 210_000, 'AirwayOpen': True},
                'BloodFlow': {'Response': 1, 'IllustrateAfSystemicPulseDeficit': False},
            },
            'Intervals': [{'FromSimTimeNs': i * 8_000_000, 'ToExclusiveSimTimeNs': (i + 1) * 8_000_000,
                'DeliveredVolumeNanolitersBtps': 1_920_000, 'AlveolarVentilationNanolitersBtps': 1_280_000,
                'EffectiveBloodVolumeNanoliters': 2_222_233 if 30 <= i < 60 else 0,
                'InspiredOxygenMillionths': 210_000, 'AirwayOpen': True} for i in range(100)],
        }

    def test_replay_uses_integrated_volumes_and_preserves_original_clock(self):
        result = replay(self.trace, self.config)
        self.assertEqual(result['Oxygenation']['SampleStepNs'], 8_000_000)
        self.assertEqual(len(result['Oxygenation']['SaturationMilliPercent']), 101)
        self.assertAlmostEqual(result['EffectiveParameters']['cardiac_output_ml_per_min'], 5000.02425)
        self.assertLess(result['Validation']['MaximumOxygenBalanceErrorMlStpd'], 1e-6)
        shifted = copy.deepcopy(self.trace)
        offset = 1_234_567_890_000
        shifted['Transport']['Physiology']['EpochAnchorSimTimeNs'] = offset
        for row in shifted['Intervals']:
            row['FromSimTimeNs'] += offset
            row['ToExclusiveSimTimeNs'] += offset
        moved = replay(shifted, self.config)
        self.assertEqual(moved['Oxygenation']['StartSimTimeNs'], offset)
        self.assertEqual(moved['Oxygenation']['SaturationMilliPercent'], result['Oxygenation']['SaturationMilliPercent'])

    def test_step_flow_is_not_replaced_with_reference_mean(self):
        parameters, control = Parameters(), Control()
        state = steady_state(parameters, control)
        closed = Control(tidal_volume_ml_btps=0, airway_open=False)
        mean = derivative(state, parameters, closed, 0)
        between = derivative(state, parameters, closed, 0, 0)
        self.assertNotEqual(mean.alveolar_oxygen_ml_stpd_per_s, between.alveolar_oxygen_ml_stpd_per_s)
        self.assertEqual(between.arterial_oxygen_ml_stpd_per_s, 0)
        self.assertEqual(between.alveolar_oxygen_ml_stpd_per_s, 0)  # Closed airway isolates blood/gas exchange.
        self.assertGreater(between.consumed_oxygen_ml_stpd_per_s, 0)

    def test_bad_clock_units_and_closed_airway_are_rejected_without_mutation(self):
        for field, value in [('FromSimTimeNs', 1), ('ToExclusiveSimTimeNs', 9_000_000),
                             ('EffectiveBloodVolumeNanoliters', -1), ('DeliveredVolumeNanolitersBtps', True),
                             ('AlveolarVentilationNanolitersBtps', 3_000_000), ('AirwayOpen', False)]:
            broken = copy.deepcopy(self.trace)
            broken['Intervals'][1][field] = value
            before = copy.deepcopy(broken)
            with self.subTest(field=field), self.assertRaises(ValueError):
                replay(broken, self.config)
            self.assertEqual(broken, before)

    def test_unvalidated_arrhythmia_and_no_flow_plans_are_not_replayed(self):
        for field, value in [('VentricularMechanicalEnabled', False), ('MechanicalEveryCycles', 2), ('ConductionPattern', 4)]:
            broken = copy.deepcopy(self.trace)
            broken['Transport']['Physiology'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                replay(broken, self.config)


if __name__ == '__main__':
    unittest.main()

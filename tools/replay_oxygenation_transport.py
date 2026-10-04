#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Replay C# event-integrated gas/blood inputs through the offline oxygen model.

This is an interop experiment, not an in-process deterministic oxygen solver.
Input steps are 8 ms. Output SaO2 uses the identical source-time grid; no sensor
or acquisition delay is added. Restrict to regular adult circulating examples.
"""
import argparse
from dataclasses import asdict, replace
import hashlib
import json
from pathlib import Path
import sys

from oxygenation_model import BTPS_TO_STPD, Control, rk4_step, sample, steady_state
from run_oxygenation_prototype import ROOT, configured_parameters


def integer(value, name, minimum, maximum):
    if isinstance(value, bool) or not isinstance(value, int) or not minimum <= value <= maximum:
        raise ValueError(f'{name} must be an integer in [{minimum}, {maximum}]')
    return value


def replay(trace, configuration):
    if trace['Format'] != 'OxygenTransportReplayInput@1':
        raise ValueError('unsupported input format')
    step_ns = integer(trace['SampleStepNs'], 'SampleStepNs', 8_000_000, 8_000_000)
    transport = trace['Transport']
    if transport['ModelId'] != 'PhysiologyTransportInputs@1':
        raise ValueError('unsupported transport model')
    physiology = transport['Physiology']
    ventilation = transport['Ventilation']
    blood = transport['BloodFlow']
    if (physiology['ConductionPattern'] != 0 or physiology['SeededRate'] is not None or
            physiology['CardiacActivity'] not in (0, 3) or not physiology['VentricularMechanicalEnabled'] or
            physiology['MechanicalAfterCycles'] is not None or physiology['MechanicalDurationCycles'] is not None or
            physiology['MechanicalEveryCycles'] != 1 or physiology['VentricularConductionRatio'] != 1 or
            physiology['ConductedBeatsPerGroup'] != 1 or physiology['IndependentVentricularPeriodNs'] is not None or
            blood['Response'] not in (0, 1) or blood['IllustrateAfSystemicPulseDeficit']):
        raise ValueError('offline replay requires regular uninterrupted circulation; other inputs remain interface-only')
    start = integer(physiology['EpochAnchorSimTimeNs'], 'epoch', 0, 2**63 - 1)
    period = integer(physiology['BreathPeriodNs'], 'breath period', 2_000_000_000, 7_500_000_000)
    inspiration = integer(physiology['InspirationDurationNs'], 'inspiration', 1, period - 1)
    hold = integer(physiology['InspiratoryPauseNs'], 'inspiratory hold', 0, inspiration - 1)
    heart = integer(physiology['HeartPeriodNs'], 'heart period', step_ns, 2_000_000_000)
    if heart % step_ns:
        raise ValueError('reference heart period must align to the replay grid')
    rows = trace['Intervals']
    if not isinstance(rows, list) or not heart // step_ns <= len(rows) <= 450_000:
        raise ValueError('replay needs at least one cardiac cycle and at most one hour')
    for index, row in enumerate(rows):
        begin = integer(row['FromSimTimeNs'], 'interval start', 0, 2**63 - 1)
        end = integer(row['ToExclusiveSimTimeNs'], 'interval end', 0, 2**63 - 1)
        if begin != start + index * step_ns or end != begin + step_ns:
            raise ValueError('transport intervals must be contiguous on the source-time grid')
        delivered = integer(row['DeliveredVolumeNanolitersBtps'], 'delivered volume', 0, 100_000_000)
        integer(row['AlveolarVentilationNanolitersBtps'], 'alveolar volume', 0, delivered)
        integer(row['EffectiveBloodVolumeNanoliters'], 'blood volume', 0, 16_000_000)
        integer(row['InspiredOxygenMillionths'], 'FiO2', 100_000, 1_000_000)
        if not isinstance(row['AirwayOpen'], bool) or not row['AirwayOpen'] and delivered:
            raise ValueError('closed airway cannot deliver gas')
    parameters, _ = configured_parameters(configuration)
    first_cycle_blood_ml = sum(row['EffectiveBloodVolumeNanoliters'] for row in rows[:heart // step_ns]) / 1e6
    parameters = replace(parameters, respiratory_rate_per_min=60e9 / period,
                         inspiration_fraction=(inspiration - hold) / period,
                         cardiac_output_ml_per_min=first_cycle_blood_ml * 60e9 / heart,
                         dead_space_ml_btps=ventilation['DeadSpaceMicrolitersBtps'] / 1000)
    control = Control(ventilation['InspiredOxygenMillionths'] / 1e6,
                      ventilation['TidalVolumeMicrolitersBtps'] / 1000, ventilation['AirwayOpen'])
    state = steady_state(parameters, control)
    reference = state.conserved_oxygen_ml
    saturations = [round(sample(start // 1000, state, parameters, reference).arterial_saturation_percent * 1000)]
    maximum_error = 0.0
    for row in rows:
        seconds = step_ns / 1e9
        control = Control(row['InspiredOxygenMillionths'] / 1e6, 0, row['AirwayOpen'])
        washout = row['AlveolarVentilationNanolitersBtps'] / 1e6 * BTPS_TO_STPD / seconds
        flow = row['EffectiveBloodVolumeNanoliters'] / 1e6 / seconds
        state = rk4_step(state, parameters, control, washout, seconds, flow)
        result = sample(row['ToExclusiveSimTimeNs'] // 1000, state, parameters, reference)
        saturations.append(round(result.arterial_saturation_percent * 1000))
        maximum_error = max(maximum_error, abs(result.oxygen_balance_error_ml))
    if maximum_error >= 1e-6:
        raise ValueError('oxygen balance exceeds replay budget')
    return {
        'Format': 'OxygenTransportReplayResult@1', 'Transport': transport,
        'Oxygenation': {'StartSimTimeNs': start, 'SampleStepNs': step_ns,
                       'SaturationMilliPercent': saturations, 'ModelId': 'SampledArterialOxygenation@1'},
        'EffectiveParameters': asdict(parameters),
        'Validation': {'MaximumOxygenBalanceErrorMlStpd': maximum_error,
                       'MinimumSao2MilliPercent': min(saturations),
                       'FinalSao2MilliPercent': saturations[-1],
                       'InitialCondition': 'analytic mean-flow steady state; no periodic warmup',
                       'Scope': 'offline pulsatile-input experiment; no clinical calibration or no-flow validation'},
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--config', type=Path, default=ROOT / 'eng/physiology/oxygenation-prototype.json')
    args = parser.parse_args()
    try:
        raw = args.input.read_bytes()
        configuration = json.loads(args.config.read_text(encoding='utf-8'))
        result = replay(json.loads(raw.decode('utf-8')), configuration)
        result['InputSha256'] = hashlib.sha256(raw).hexdigest()
        result['Configuration'] = configuration
        result['SourceSha256'] = {name: hashlib.sha256((ROOT / 'tools' / name).read_bytes()).hexdigest()
                                 for name in ('oxygenation_model.py', 'run_oxygenation_prototype.py', 'replay_oxygenation_transport.py')}
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf-8')
        print(f"PASS: {len(result['Oxygenation']['SaturationMilliPercent'])} source-time oxygenation samples; output: {args.output}")
        return 0
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        print(f'Oxygen transport replay failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())

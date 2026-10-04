#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Run the offline oxygen parameter study; write CSV/JSON and optional plots."""
import argparse
import csv
from dataclasses import asdict, replace
import hashlib
import json
from pathlib import Path
import platform
import sys

from oxygenation_model import (
    AdultProfile, BTPS_TO_STPD, Control, MODEL_ID, Parameters, Phase,
    equilibrate, simulate, with_blood_volume,
)

ROOT = Path(__file__).resolve().parent.parent


def configured_parameters(configuration):
    if configuration['model_id'] != MODEL_ID:
        raise ValueError('unsupported model_id')
    parameters = Parameters(**configuration['parameters'])
    blood = configuration['blood_volume']
    if blood['method'] == 'nadler_adult':
        total = AdultProfile(**blood['profile']).blood_volume_ml()
    elif blood['method'] == 'explicit':
        total = blood['total_volume_ml']
    else:
        raise ValueError('blood_volume.method must be nadler_adult or explicit')
    parameters = with_blood_volume(parameters, total, blood['arterial_fraction'])
    return parameters, Control(**configuration['reference_control'])


def apnea_protocol(control, airway_open=False, duration_seconds=90):
    return [Phase(30, control), Phase(duration_seconds, replace(control, tidal_volume_ml_btps=0, airway_open=airway_open)),
            Phase(330, control)]


def crossing(samples, threshold_percent, start_seconds=30, end_inclusive_seconds=120):
    """First downward crossing within inclusive bounds, relative to start_seconds.

    None means no crossing was observed.
    """
    window = [s for s in samples if start_seconds <= s.time_seconds <= end_inclusive_seconds]
    if window[0].arterial_saturation_percent <= threshold_percent:
        return 0.0
    for left, right in zip(window, window[1:]):
        if right.arterial_saturation_percent <= threshold_percent < left.arterial_saturation_percent:
            fraction = ((left.arterial_saturation_percent - threshold_percent) /
                        (left.arterial_saturation_percent - right.arterial_saturation_percent))
            return left.time_seconds + fraction * (right.time_seconds - left.time_seconds) - start_seconds
    return None


def case_summary(name, parameters, phases, samples, cycles, breath_resolved):
    return {
        'name': name,
        'parameters': asdict(parameters),
        'phases': [asdict(phase) for phase in phases],
        'breath_resolved': breath_resolved,
        'warmup_breaths': cycles,
        'initial_sao2_percent': samples[0].arterial_saturation_percent,
        'minimum_sao2_percent': min(s.arterial_saturation_percent for s in samples),
        'final_sao2_percent': samples[-1].arterial_saturation_percent,
        'seconds_to_90_after_onset': crossing(samples, 90),
        'seconds_to_85_after_onset': crossing(samples, 85),
        'crossing_observation_window_seconds': [30, 120],
        'null_crossing_means': 'not reached within the observation window; not zero or infinity',
        'maximum_oxygen_balance_error_ml_stpd': max(abs(s.oxygen_balance_error_ml) for s in samples),
        'initial_oxygen_inventory_ml_stpd': samples[0].state.oxygen_inventory_ml,
        'final_unmet_oxygen_demand_ml_stpd': samples[-1].state.unmet_oxygen_demand_ml,
    }


def run_study(parameters, control, step_seconds):
    cases, traces, initializations = [], {}, {}

    def add(name, current=parameters, phases=None, breath_resolved=True, preparation=None):
        key = (current, control, breath_resolved)
        if key not in initializations:
            initializations[key] = equilibrate(current, control, step_seconds, breath_resolved)
        state, cycles = initializations[key]
        if preparation:
            state = simulate(current, preparation, state, step_seconds, breath_resolved=breath_resolved)[-1].state.without_ledgers()
        phases = phases or apnea_protocol(control)
        rows = simulate(current, phases, state, step_seconds, breath_resolved=breath_resolved)
        traces[name] = rows
        summary = case_summary(name, current, phases, rows, cycles, breath_resolved)
        summary['preparation'] = [asdict(phase) for phase in preparation or []]
        cases.append(summary)

    add('steady_mean', phases=[Phase(360, control)], breath_resolved=False)
    add('normal_breaths', phases=[Phase(360, control)])
    add('apnea_recovery')
    add('low_ventilation', phases=[Phase(30, control), Phase(180, replace(control, tidal_volume_ml_btps=250)), Phase(330, control)])
    add('low_inspired_oxygen', phases=[Phase(30, control), Phase(180, replace(control, inspired_oxygen_fraction=0.15)), Phase(330, control)])
    repeated = [Phase(30, control)]
    for _ in range(3):
        repeated.extend([Phase(30, replace(control, tidal_volume_ml_btps=0, airway_open=False)), Phase(30, control)])
    add('repeated_apnea', phases=repeated + [Phase(270, control)])
    add('open_air_apnea', phases=apnea_protocol(control, airway_open=True))
    oxygen = replace(control, inspired_oxygen_fraction=1.0)
    # 150 s before recording + 30 s recorded baseline = 180 s before apnea.
    add('preoxygenated_apnea', phases=apnea_protocol(oxygen)[:-1] + [Phase(330, control)],
        preparation=[Phase(150, oxygen)])
    add('open_oxygen_apnea', phases=[Phase(30, control), Phase(90, replace(oxygen, tidal_volume_ml_btps=0)), Phase(330, control)])
    for field, values, prefix in [
        ('frc_ml_btps', (1100, 3300), 'frc'),
        ('oxygen_demand_ml_stpd_per_min', (150, 350), 'demand'),
        ('hemoglobin_g_per_dl', (10, 18), 'hb'),
        ('cardiac_output_ml_per_min', (3000, 7000), 'flow'),
        ('shunt_fraction', (0.0, 0.1), 'shunt'),
        ('dead_space_ml_btps', (100, 200), 'dead_space'),
        ('consumption_floor_content_ml_per_dl', (1, 4), 'consumption_floor'),
        ('volume_recovery_seconds', (0.5, 5), 'volume_recovery'),
    ]:
        for value in values:
            add(f'{prefix}_{value}', current=replace(parameters, **{field: value}))
    for total in (3500, 6500):
        add(f'blood_volume_{total}', current=with_blood_volume(parameters, total))
    total = parameters.arterial_volume_ml + parameters.venous_volume_ml
    for fraction in (0.1, 0.3):
        add(f'arterial_fraction_{fraction}', current=with_blood_volume(parameters, total, fraction))
    # Change volume alone; these are NOT fully individualized virtual patients.
    for sex in ('male', 'female'):
        profile = AdultProfile(35, sex, 170, 70)
        add(f'nadler_{sex}_170cm_70kg', current=with_blood_volume(parameters, profile.blood_volume_ml()))
    add('long_apnea_numerical_stress', phases=[Phase(30, control),
        Phase(1200, replace(control, tidal_volume_ml_btps=0, airway_open=False)), Phase(330, control)])
    return cases, traces


def validate_study(parameters, control, step_seconds, cases, traces):
    checks = []

    def check(name, passed, observed):
        checks.append({'name': name, 'passed': bool(passed), 'observed': observed})

    by_name = {case['name']: case for case in cases}
    error = max(case['maximum_oxygen_balance_error_ml_stpd'] for case in cases)
    check('oxygen_conservation_all_cases_lt_1e-6_ml', error < 1e-6, error)
    normal = traces['steady_mean']
    drift = max(abs(s.arterial_saturation_percent - normal[0].arterial_saturation_percent) for s in normal)
    check('analytic_mean_steady_state_drift_lt_1e-7_pp', drift < 1e-7, drift)
    # End 360 s is exactly 96 breaths for the declared default; choose a complete
    # period directly as well, so parameter-file RR changes remain supported.
    initial, _ = equilibrate(parameters, control, step_seconds)
    cycle = simulate(parameters, [Phase(parameters.period_us / 1e6, control)], initial, step_seconds)
    drift_ml = max(abs(a - b) for a, b in zip(cycle[0].state[:5], cycle[-1].state[:5], strict=True))
    check('periodic_initialization_drift_lt_1e-7_ml', drift_ml < 1e-7, drift_ml)
    apnea = traces['apnea_recovery']
    left = next(s for s in apnea if s.time_seconds == 30).state
    right = next(s for s in apnea if s.time_seconds == 120).state
    net_loss = left.oxygen_inventory_ml - right.oxygen_inventory_ml
    demand = parameters.oxygen_demand_ml_stpd_per_min * 90 / 60
    unfulfilled = right.unmet_oxygen_demand_ml - left.unmet_oxygen_demand_ml
    check('closed_apnea_has_no_external_oxygen',
          right.inspired_oxygen_ml == left.inspired_oxygen_ml and right.expired_oxygen_ml == left.expired_oxygen_ml,
          [right.inspired_oxygen_ml - left.inspired_oxygen_ml, right.expired_oxygen_ml - left.expired_oxygen_ml])
    check('closed_inventory_loss_equals_actual_metabolism', abs(net_loss - (demand - unfulfilled)) < 1e-7,
          {'inventory_loss_ml': net_loss, 'actual_metabolism_ml': demand - unfulfilled})
    for label, earlier, later in [('frc', 'frc_1100', 'frc_3300'), ('demand', 'demand_350', 'demand_150'),
                                   ('blood_volume', 'blood_volume_3500', 'blood_volume_6500'), ('hb', 'hb_10', 'hb_18')]:
        values = [by_name[name]['seconds_to_90_after_onset'] for name in (earlier, later)]
        check(f'{label}_sensitivity_order', values[0] is not None and (values[1] is None or values[0] < values[1]), values)
    check('preoxygenation_increases_initial_oxygen_inventory',
          by_name['preoxygenated_apnea']['initial_oxygen_inventory_ml_stpd'] > by_name['apnea_recovery']['initial_oxygen_inventory_ml_stpd'],
          [by_name[name]['initial_oxygen_inventory_ml_stpd'] for name in ('apnea_recovery', 'preoxygenated_apnea')])
    for name in ('apnea_recovery', 'low_ventilation', 'low_inspired_oxygen'):
        case = by_name[name]
        check(f'{name}_desaturates_and_recovers',
              case['minimum_sao2_percent'] < case['initial_sao2_percent'] - 2 and
              abs(case['final_sao2_percent'] - case['initial_sao2_percent']) < 0.5,
              [case['initial_sao2_percent'], case['minimum_sao2_percent'], case['final_sao2_percent']])
    stress = traces['long_apnea_numerical_stress']
    check('depletion_limits_actual_consumption_and_retains_positive_stores',
          stress[-1].state.unmet_oxygen_demand_ml > 0 and min(min(s.state[:5]) for s in stress) >= 0,
          {'unmet_demand_ml': stress[-1].state.unmet_oxygen_demand_ml,
           'minimum_store_ml': min(min(s.state[:5]) for s in stress)})
    convergence = []
    for divisor in (2, 4):
        smaller_step = step_seconds / divisor
        fine_initial, _ = equilibrate(parameters, control, smaller_step)
        fine = simulate(parameters, apnea_protocol(control), fine_initial, smaller_step)
        difference = max(abs(a.arterial_saturation_percent - b.arterial_saturation_percent)
                         for a, b in zip(apnea, fine, strict=True))
        convergence.append({'step_seconds': smaller_step, 'maximum_sao2_difference_pp': difference})
    check('step_refinement_changes_sao2_lt_0.01_pp', all(row['maximum_sao2_difference_pp'] < 0.01 for row in convergence), convergence)
    return checks


def write_results(output, configuration, parameters, step_seconds, cases, traces, checks):
    output.mkdir(parents=True, exist_ok=True)
    header = ['time_seconds', 'sao2_percent', 'pao2_mmhg', 'svo2_percent', 'alveolar_po2_mmhg',
              'lung_volume_ml_btps', 'oxygen_inventory_ml_stpd', 'oxygen_balance_error_ml_stpd',
              'inspired_oxygen_ml_stpd', 'expired_oxygen_ml_stpd', 'consumed_oxygen_ml_stpd', 'unmet_oxygen_demand_ml_stpd']
    for name, rows in traces.items():
        with (output / f'{name}.csv').open('w', encoding='utf-8', newline='') as stream:
            writer = csv.writer(stream)
            writer.writerow(header)
            for s in rows:
                writer.writerow([s.time_seconds, s.arterial_saturation_percent, s.arterial_pressure_mmhg,
                                 s.venous_saturation_percent, s.alveolar_pressure_mmhg, s.gas_volume_ml_btps,
                                 s.state.oxygen_inventory_ml, s.oxygen_balance_error_ml,
                                 *s.state[5:]])
    report = {
        'model_id': MODEL_ID, 'python_version': platform.python_version(),
        'configuration': configuration, 'effective_parameters': asdict(parameters),
        'step_seconds': step_seconds, 'sample_seconds': 0.5, 'btps_to_stpd': BTPS_TO_STPD,
        'scope': 'offline adult circulating model; SaO2 is latent, not a measured SpO2; no clinical calibration',
        'source_sha256': {name: hashlib.sha256((ROOT / 'tools' / name).read_bytes()).hexdigest()
                          for name in ('oxygenation_model.py', 'run_oxygenation_prototype.py')},
        'cases': cases, 'checks': checks, 'all_checks_passed': all(check['passed'] for check in checks),
    }
    (output / 'results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    with (output / 'summary.csv').open('w', encoding='utf-8', newline='') as stream:
        names = ['name', 'initial_sao2_percent', 'minimum_sao2_percent', 'final_sao2_percent',
                 'seconds_to_90_after_onset', 'seconds_to_85_after_onset', 'maximum_oxygen_balance_error_ml_stpd']
        writer = csv.DictWriter(stream, names, extrasaction='ignore')
        writer.writeheader()
        writer.writerows(cases)
    return report


def plot_results(output, traces):
    # Optional visualization dependency; core simulation/tests use only stdlib.
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt

    plt.rcParams.update({'font.size': 10, 'axes.spines.top': False, 'axes.spines.right': False,
                         'svg.hashsalt': MODEL_ID})
    figure, axes = plt.subplots(2, 2, figsize=(13, 8), layout='constrained')
    groups = [
        [('normal_breaths', 'Normal ventilation'), ('apnea_recovery', 'Closed apnea'),
         ('preoxygenated_apnea', '180 s preoxygenation')],
        [('apnea_recovery', 'Reference'), ('frc_1100', 'FRC 1.1 L'), ('frc_3300', 'FRC 3.3 L'),
         ('demand_350', 'Demand 350 mL/min')],
        [('apnea_recovery', 'Reference blood volume'), ('blood_volume_3500', 'Blood 3.5 L'),
         ('blood_volume_6500', 'Blood 6.5 L'), ('hb_10', 'Hb 10 g/dL')],
        [('low_ventilation', 'Delivered VT 250 mL'), ('low_inspired_oxygen', 'Inspired O2 15%'),
         ('repeated_apnea', 'Repeated 30 s apneas')],
    ]
    titles = ['Oxygen stores and recovery', 'Lung stores and metabolic demand',
              'Blood volume and hemoglobin', 'Ventilation and oxygen supply']
    for index, (axis, group, title) in enumerate(zip(axes.flat, groups, titles, strict=True)):
        for name, label in group:
            rows = traces[name]
            axis.plot([s.time_seconds for s in rows], [s.arterial_saturation_percent for s in rows], label=label, linewidth=1.7)
        if index < 3:
            axis.axvspan(30, 120, color='#d96c55', alpha=0.12, label='No ventilation (30–120 s)')
        axis.set(title=title, xlabel='Simulation time (s)', ylabel='Model SaO2 (%)', ylim=(35, 101))
        axis.axhline(90, color='#6d7180', linestyle=':', linewidth=0.8)
        axis.grid(alpha=0.2)
        axis.legend(fontsize=8, loc='lower right')
    figure.suptitle('Independent oxygen transport prototype — circulating adult scenarios', fontsize=15)
    figure.supxlabel('Latent SaO2, not measured SpO2. Authored reduced model; no clinical calibration.', fontsize=10)
    for suffix in ('png', 'svg'):
        figure.savefig(output / f'oxygenation-overview.{suffix}', dpi=170)
    plt.close(figure)
    (output / 'plot-environment.json').write_text(json.dumps({'matplotlib': matplotlib.__version__}, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=ROOT / 'eng/physiology/oxygenation-prototype.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/oxygenation-prototype')
    parser.add_argument('--step-ms', type=float, default=50)
    parser.add_argument('--plot', action='store_true', help='Requires optional matplotlib (tested with 3.10.7)')
    parser.add_argument('--blood-volume-ml', type=float, help='Explicit scenario total, overrides demographic estimation')
    parser.add_argument('--age-years', type=float)
    parser.add_argument('--sex', choices=('male', 'female'), help='Nadler formula coefficient selection')
    parser.add_argument('--height-cm', type=float)
    parser.add_argument('--weight-kg', type=float)
    args = parser.parse_args()
    demographic = (args.age_years, args.sex, args.height_cm, args.weight_kg)
    if any(value is not None for value in demographic) and (not all(value is not None for value in demographic) or args.blood_volume_ml is not None):
        parser.error('supply all four demographic fields, or --blood-volume-ml, not both')
    if not 4 <= args.step_ms <= 50:
        parser.error('--step-ms must be 4..50 to allow two step-refinement runs')
    try:
        configuration = json.loads(args.config.read_text(encoding='utf-8'))
        if args.blood_volume_ml is not None:
            configuration['blood_volume'] = {'method': 'explicit', 'total_volume_ml': args.blood_volume_ml,
                                             'arterial_fraction': configuration['blood_volume']['arterial_fraction']}
        elif args.age_years is not None:
            configuration['blood_volume'] = {'method': 'nadler_adult',
                'profile': {'age_years': args.age_years, 'formula_sex': args.sex,
                            'height_cm': args.height_cm, 'weight_kg': args.weight_kg},
                'arterial_fraction': configuration['blood_volume']['arterial_fraction']}
        parameters, control = configured_parameters(configuration)
        step_seconds = args.step_ms / 1000
        cases, traces = run_study(parameters, control, step_seconds)
        checks = validate_study(parameters, control, step_seconds, cases, traces)
        report = write_results(args.output, configuration, parameters, step_seconds, cases, traces, checks)
        if args.plot:
            plot_results(args.output, traces)
        for check in checks:
            print(f'{"PASS" if check["passed"] else "FAIL"}: {check["name"]}')
        print(f'{len(cases)} scenarios; blood volume {parameters.arterial_volume_ml + parameters.venous_volume_ml:.1f} mL; output: {args.output}')
        return 0 if report['all_checks_passed'] else 1
    except (OSError, ValueError, TypeError, KeyError, ImportError) as error:
        print(f'Oxygen prototype failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())

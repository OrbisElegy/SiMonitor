# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline floating-point oxygen prototype; no monitor or sensor dependency.

Three reservoirs: isobaric mixed alveolar gas, arterial blood, systemic/venous
blood. End-capillary oxygen equilibrates with alveolar gas. Systemic consumption
is lumped into venous blood. This deliberately excludes low/no-flow circulation,
separate tissue stores, blood CO2 buffering, pH feedback and lung mechanics.
"""
from dataclasses import dataclass, fields, replace
import math
from typing import NamedTuple


MODEL_ID = 'OxygenTransportPrototype@1'
DRY_PRESSURE_MMHG = 760.0 - 47.0
BTPS_TO_STPD = (273.15 / 310.15) * (DRY_PRESSURE_MMHG / 760.0)
MICROSECONDS_PER_SECOND = 1_000_000


def bounded(name, value, minimum, maximum):
    if (isinstance(value, bool) or not isinstance(value, (int, float)) or
            not math.isfinite(value) or not minimum <= value <= maximum):
        raise ValueError(f'{name} must be finite and in [{minimum}, {maximum}]')


@dataclass(frozen=True)
class Parameters:
    frc_ml_btps: float = 2200.0
    dead_space_ml_btps: float = 150.0
    respiratory_rate_per_min: float = 16.0
    inspiration_fraction: float = 0.4
    cardiac_output_ml_per_min: float = 5000.0
    arterial_volume_ml: float = 1000.0
    venous_volume_ml: float = 4000.0
    hemoglobin_g_per_dl: float = 15.0
    shunt_fraction: float = 0.02
    oxygen_demand_ml_stpd_per_min: float = 250.0
    respiratory_quotient: float = 0.8
    consumption_floor_content_ml_per_dl: float = 2.0
    volume_recovery_seconds: float = 2.0

    def __post_init__(self):
        # Supported experiment envelope, not normal ranges or clinical limits.
        limits = ((500, 5000), (0, 300), (8, 30), (0.2, 0.6), (2500, 8000),
                  (250, 2250), (1750, 6750), (6, 20), (0, 0.3), (0, 500),
                  (0.7, 1.0), (1, 4), (0.5, 10))
        for field, (minimum, maximum) in zip(fields(self), limits, strict=True):
            bounded(field.name, getattr(self, field.name), minimum, maximum)

    @property
    def period_us(self):
        return round(60 * MICROSECONDS_PER_SECOND / self.respiratory_rate_per_min)

    @property
    def inspiration_us(self):
        return round(self.period_us * self.inspiration_fraction)

    @property
    def reference_gas_ml_stpd(self):
        return self.frc_ml_btps * BTPS_TO_STPD


@dataclass(frozen=True)
class Control:
    inspired_oxygen_fraction: float = 0.21
    tidal_volume_ml_btps: float = 450.0
    airway_open: bool = True

    def __post_init__(self):
        bounded('inspired_oxygen_fraction', self.inspired_oxygen_fraction, 0.1, 1.0)
        bounded('tidal_volume_ml_btps', self.tidal_volume_ml_btps, 0, 1000)
        if not isinstance(self.airway_open, bool):
            raise ValueError('airway_open must be boolean')
        if not self.airway_open and self.tidal_volume_ml_btps != 0:
            raise ValueError('closed airway requires zero delivered tidal volume')


@dataclass(frozen=True)
class AdultProfile:
    age_years: float
    formula_sex: str
    height_cm: float
    weight_kg: float

    def __post_init__(self):
        # Deliberately narrow adult prototype admission, not Nadler's original
        # population bounds. Age selects eligibility; it is not a formula term.
        bounded('age_years', self.age_years, 18, 90)
        bounded('height_cm', self.height_cm, 140, 210)
        bounded('weight_kg', self.weight_kg, 40, 150)
        if self.formula_sex not in ('male', 'female'):
            raise ValueError('select male/female Nadler coefficients or supply explicit blood volume')
        if not 18.5 <= self.bmi < 30:
            raise ValueError('Nadler prototype limited to BMI [18.5, 30); use an explicit scenario volume outside this range')

    @property
    def bmi(self):
        return self.weight_kg / (self.height_cm / 100) ** 2

    def blood_volume_ml(self):
        coefficients = (0.3669, 0.03219, 0.6041) if self.formula_sex == 'male' else (0.3561, 0.03308, 0.1833)
        height, weight, constant = coefficients
        return 1000 * (height * (self.height_cm / 100) ** 3 + weight * self.weight_kg + constant)


def with_blood_volume(parameters, total_volume_ml, arterial_fraction=0.2):
    bounded('total_volume_ml', total_volume_ml, 2500, 7500)
    bounded('arterial_fraction', arterial_fraction, 0.1, 0.3)
    # The split is an independent effective-reservoir assumption, not an output
    # of the demographic equation and not an anatomical vascular volume claim.
    return replace(parameters, arterial_volume_ml=total_volume_ml * arterial_fraction,
                   venous_volume_ml=total_volume_ml * (1 - arterial_fraction))


@dataclass(frozen=True)
class Phase:
    duration_seconds: float
    control: Control

    def __post_init__(self):
        time_us(self.duration_seconds, 'duration_seconds')
        if not isinstance(self.control, Control):
            raise ValueError('phase requires a Control')


class State(NamedTuple):
    # All gas amounts and oxygen ledgers are mL at STPD, not lung BTPS volume.
    alveolar_oxygen_ml: float
    alveolar_carbon_dioxide_ml: float
    alveolar_inert_gas_ml: float
    arterial_oxygen_ml: float
    venous_oxygen_ml: float
    inspired_oxygen_ml: float = 0.0
    expired_oxygen_ml: float = 0.0
    consumed_oxygen_ml: float = 0.0
    unmet_oxygen_demand_ml: float = 0.0

    @property
    def gas_ml_stpd(self):
        return sum(self[:3])

    @property
    def oxygen_inventory_ml(self):
        return self.alveolar_oxygen_ml + self.arterial_oxygen_ml + self.venous_oxygen_ml

    @property
    def conserved_oxygen_ml(self):
        return (self.oxygen_inventory_ml - self.inspired_oxygen_ml +
                self.expired_oxygen_ml + self.consumed_oxygen_ml)

    def without_ledgers(self):
        return State(*self[:5])


class StateRates(NamedTuple):
    """Signed reservoir and oxygen-ledger rates, all in STPD mL/s."""
    alveolar_oxygen_ml_stpd_per_s: float
    alveolar_carbon_dioxide_ml_stpd_per_s: float
    alveolar_inert_gas_ml_stpd_per_s: float
    arterial_oxygen_ml_stpd_per_s: float
    venous_oxygen_ml_stpd_per_s: float
    inspired_oxygen_ml_stpd_per_s: float
    expired_oxygen_ml_stpd_per_s: float
    consumed_oxygen_ml_stpd_per_s: float
    unmet_oxygen_demand_ml_stpd_per_s: float

    @property
    def gas_ml_stpd_per_s(self):
        return sum(self[:3])

    @property
    def oxygen_inventory_ml_stpd_per_s(self):
        return (self.alveolar_oxygen_ml_stpd_per_s + self.arterial_oxygen_ml_stpd_per_s +
                self.venous_oxygen_ml_stpd_per_s)

    @property
    def conserved_oxygen_ml_stpd_per_s(self):
        return (self.oxygen_inventory_ml_stpd_per_s - self.inspired_oxygen_ml_stpd_per_s +
                self.expired_oxygen_ml_stpd_per_s + self.consumed_oxygen_ml_stpd_per_s)


@dataclass(frozen=True)
class Sample:
    time_seconds: float
    state: State
    arterial_saturation_percent: float
    arterial_pressure_mmhg: float
    venous_saturation_percent: float
    alveolar_pressure_mmhg: float
    gas_volume_ml_btps: float
    oxygen_balance_error_ml: float


def saturation_fraction(pressure_mmhg):
    bounded('pressure_mmhg', pressure_mmhg, 0, 1000)
    numerator = pressure_mmhg ** 3 + 150 * pressure_mmhg
    return numerator / (numerator + 23400)


def oxygen_content_ml_per_ml(pressure_mmhg, hemoglobin_g_per_dl):
    bounded('hemoglobin_g_per_dl', hemoglobin_g_per_dl, 6, 20)
    return (1.34 * hemoglobin_g_per_dl * saturation_fraction(pressure_mmhg) +
            0.0031 * pressure_mmhg) / 100


def pressure_from_content(content_ml_per_ml, hemoglobin_g_per_dl):
    bounded('content_ml_per_ml', content_ml_per_ml, 0,
            oxygen_content_ml_per_ml(1000, hemoglobin_g_per_dl))
    low, high = 0.0, 1000.0
    for _ in range(48):
        middle = (low + high) / 2
        if oxygen_content_ml_per_ml(middle, hemoglobin_g_per_dl) < content_ml_per_ml:
            low = middle
        else:
            high = middle
    return (low + high) / 2


def time_us(seconds, name):
    bounded(name, seconds, 0.000001, 3600)
    value = round(seconds * MICROSECONDS_PER_SECOND)
    if abs(value - seconds * MICROSECONDS_PER_SECOND) > 1e-6:
        raise ValueError(f'{name} must resolve to whole microseconds')
    return value


def validate_state(state, parameters):
    if not isinstance(state, State) or any(not math.isfinite(v) or v < 0 for v in state):
        raise ValueError('non-finite or negative store/ledger; state was not clipped')
    if not 1 < state.gas_ml_stpd < 2 * parameters.reference_gas_ml_stpd:
        raise ValueError('alveolar gas volume outside prototype envelope')
    maximum = oxygen_content_ml_per_ml(1000, parameters.hemoglobin_g_per_dl)
    if (state.arterial_oxygen_ml > parameters.arterial_volume_ml * maximum or
            state.venous_oxygen_ml > parameters.venous_volume_ml * maximum):
        raise ValueError('blood oxygen content outside prototype envelope')


def ventilation_ml_stpd_per_s(parameters, control, at_us, breath_resolved):
    effective_ml = max(0.0, control.tidal_volume_ml_btps - parameters.dead_space_ml_btps)
    if breath_resolved:
        if at_us % parameters.period_us >= parameters.inspiration_us:
            return 0.0
        duration_seconds = parameters.inspiration_us / MICROSECONDS_PER_SECOND
    else:
        duration_seconds = parameters.period_us / MICROSECONDS_PER_SECOND
    return effective_ml * BTPS_TO_STPD / duration_seconds


def steady_state(parameters, control):
    """Analytic, fully aerobic equilibrium of the mean-ventilation equations."""
    if not control.airway_open:
        raise ValueError('steady initialization requires an open ventilated airway')
    alveolar_ventilation_ml_stpd_per_s = ventilation_ml_stpd_per_s(parameters, control, 0, False)
    demand = parameters.oxygen_demand_ml_stpd_per_min / 60
    if alveolar_ventilation_ml_stpd_per_s <= 0:
        raise ValueError('steady initialization requires positive alveolar ventilation')
    carbon_dioxide = parameters.respiratory_quotient * demand
    inspired = alveolar_ventilation_ml_stpd_per_s + demand - carbon_dioxide
    oxygen_fraction = (inspired * control.inspired_oxygen_fraction - demand) / alveolar_ventilation_ml_stpd_per_s
    carbon_fraction = carbon_dioxide / alveolar_ventilation_ml_stpd_per_s
    inert_fraction = inspired * (1 - control.inspired_oxygen_fraction) / alveolar_ventilation_ml_stpd_per_s
    if min(oxygen_fraction, carbon_fraction, inert_fraction) < 0:
        raise ValueError('no aerobic steady state for these inputs')
    capillary_content = oxygen_content_ml_per_ml(
        oxygen_fraction * DRY_PRESSURE_MMHG, parameters.hemoglobin_g_per_dl)
    pulmonary_flow = parameters.cardiac_output_ml_per_min / 60 * (1 - parameters.shunt_fraction)
    venous_content = capillary_content - demand / pulmonary_flow
    arterial_content = capillary_content - parameters.shunt_fraction * demand / pulmonary_flow
    if venous_content < parameters.consumption_floor_content_ml_per_dl / 100:
        raise ValueError('steady oxygen delivery cannot meet full metabolic demand')
    gas = parameters.reference_gas_ml_stpd
    state = State(gas * oxygen_fraction, gas * carbon_fraction, gas * inert_fraction,
                  arterial_content * parameters.arterial_volume_ml,
                  venous_content * parameters.venous_volume_ml)
    validate_state(state, parameters)
    return state


def derivative(state, parameters, control, alveolar_ventilation_ml_stpd_per_s, blood_flow_ml_per_s=None):
    validate_state(state, parameters)
    oxygen_fraction, carbon_fraction, inert_fraction = (v / state.gas_ml_stpd for v in state[:3])
    arterial_content = state.arterial_oxygen_ml / parameters.arterial_volume_ml
    venous_content = state.venous_oxygen_ml / parameters.venous_volume_ml
    if blood_flow_ml_per_s is not None:
        # Optional event-integrated flow for the offline transport bridge.
        # Zero between ejections is admitted; sustained no-flow is not validated.
        bounded('blood_flow_ml_per_s', blood_flow_ml_per_s, 0, 2000)
    blood_flow_ml_per_s = parameters.cardiac_output_ml_per_min / 60 if blood_flow_ml_per_s is None else blood_flow_ml_per_s
    capillary_content = oxygen_content_ml_per_ml(
        oxygen_fraction * DRY_PRESSURE_MMHG, parameters.hemoglobin_g_per_dl)
    # One shared signed transfer amount is used on both sides of the lung boundary.
    lung_transfer = blood_flow_ml_per_s * (1 - parameters.shunt_fraction) * (capillary_content - venous_content)
    systemic_transfer = blood_flow_ml_per_s * (arterial_content - venous_content)
    demand = parameters.oxygen_demand_ml_stpd_per_min / 60
    consumed = demand * min(1.0, venous_content / (parameters.consumption_floor_content_ml_per_dl / 100))
    # Explicit reduced closure: CO2 reaches the gas bag immediately, without a
    # body CO2 pool or a change of the oxygen dissociation curve's pH/temperature.
    carbon_production = parameters.respiratory_quotient * consumed
    if control.airway_open:
        net_uptake = lung_transfer - carbon_production
        volume_error = (parameters.reference_gas_ml_stpd - state.gas_ml_stpd) / parameters.volume_recovery_seconds
        inspired_flow = alveolar_ventilation_ml_stpd_per_s + max(0.0, net_uptake + volume_error)
        expired_flow = alveolar_ventilation_ml_stpd_per_s + max(0.0, -net_uptake - volume_error)
    else:
        inspired_flow = expired_flow = 0.0
    inspired_oxygen = inspired_flow * control.inspired_oxygen_fraction
    expired_oxygen = expired_flow * oxygen_fraction
    return StateRates(
        inspired_oxygen - expired_oxygen - lung_transfer,
        carbon_production - expired_flow * carbon_fraction,
        inspired_flow * (1 - control.inspired_oxygen_fraction) - expired_flow * inert_fraction,
        lung_transfer - systemic_transfer,
        systemic_transfer - consumed,
        inspired_oxygen, expired_oxygen, consumed, demand - consumed)


def rk4_step(state, parameters, control, alveolar_ventilation_ml_stpd_per_s, seconds, blood_flow_ml_per_s=None):
    def shifted(slope, scale):
        return State(*(v + scale * d for v, d in zip(state, slope, strict=True)))

    first = derivative(state, parameters, control, alveolar_ventilation_ml_stpd_per_s, blood_flow_ml_per_s)
    second = derivative(shifted(first, seconds / 2), parameters, control, alveolar_ventilation_ml_stpd_per_s, blood_flow_ml_per_s)
    third = derivative(shifted(second, seconds / 2), parameters, control, alveolar_ventilation_ml_stpd_per_s, blood_flow_ml_per_s)
    fourth = derivative(shifted(third, seconds), parameters, control, alveolar_ventilation_ml_stpd_per_s, blood_flow_ml_per_s)
    result = State(*(v + seconds * (a + 2 * b + 2 * c + d) / 6
                     for v, a, b, c, d in zip(state, first, second, third, fourth, strict=True)))
    validate_state(result, parameters)
    return result


def advance(state, parameters, control, from_us, to_us, step_us, breath_resolved=True):
    """Integrate to an exact boundary; constant controls within this interval."""
    if (isinstance(from_us, bool) or isinstance(to_us, bool) or
            not isinstance(from_us, int) or not isinstance(to_us, int) or
            not 0 <= from_us <= to_us or to_us - from_us > 3600_000_000 or
            isinstance(step_us, bool) or not isinstance(step_us, int) or not 1000 <= step_us <= 50_000 or
            not isinstance(breath_resolved, bool)):
        raise ValueError('invalid integration bounds, mode or step (1–50 ms)')
    validate_state(state, parameters)
    current = from_us
    while current < to_us:
        boundary = min(to_us, (current // step_us + 1) * step_us)
        if breath_resolved:
            phase = current % parameters.period_us
            offset = parameters.inspiration_us if phase < parameters.inspiration_us else parameters.period_us
            boundary = min(boundary, current - phase + offset)
        alveolar_ventilation_ml_stpd_per_s = ventilation_ml_stpd_per_s(parameters, control, current, breath_resolved)
        state = rk4_step(state, parameters, control, alveolar_ventilation_ml_stpd_per_s,
                         (boundary - current) / MICROSECONDS_PER_SECOND)
        current = boundary
    return state


def equilibrate(parameters, control, step_seconds=0.05, breath_resolved=True):
    step_us = time_us(step_seconds, 'step_seconds')
    state = steady_state(parameters, control)
    if not breath_resolved:
        # Validate the integrator settings even if no warm-up is necessary.
        advance(state, parameters, control, 0, 0, step_us, False)
        return state, 0
    for cycle in range(1, 1025):
        following = advance(state, parameters, control, 0, parameters.period_us, step_us)
        difference = max(abs(a - b) for a, b in zip(state[:5], following[:5], strict=True))
        state = following.without_ledgers()
        if difference < 1e-8:
            return state, cycle
    raise ValueError('periodic initialization did not converge within 1024 breaths')


def sample(at_us, state, parameters, conserved_reference):
    arterial_pressure = pressure_from_content(
        state.arterial_oxygen_ml / parameters.arterial_volume_ml, parameters.hemoglobin_g_per_dl)
    venous_pressure = pressure_from_content(
        state.venous_oxygen_ml / parameters.venous_volume_ml, parameters.hemoglobin_g_per_dl)
    return Sample(at_us / MICROSECONDS_PER_SECOND, state,
                  100 * saturation_fraction(arterial_pressure), arterial_pressure,
                  100 * saturation_fraction(venous_pressure),
                  DRY_PRESSURE_MMHG * state.alveolar_oxygen_ml / state.gas_ml_stpd,
                  state.gas_ml_stpd / BTPS_TO_STPD,
                  state.conserved_oxygen_ml - conserved_reference)


def simulate(parameters, phases, initial_state=None, step_seconds=0.05,
             sample_seconds=0.5, breath_resolved=True):
    phases = tuple(phases)
    if not phases or any(not isinstance(phase, Phase) for phase in phases):
        raise ValueError('simulation needs a nonempty validated phase sequence')
    step_us = time_us(step_seconds, 'step_seconds')
    sample_us = time_us(sample_seconds, 'sample_seconds')
    durations = [time_us(phase.duration_seconds, 'duration_seconds') for phase in phases]
    if sum(durations) > 3600_000_000:
        raise ValueError('prototype run is bounded to one hour')
    if initial_state is None:
        initial_state, _ = equilibrate(parameters, phases[0].control, step_seconds, breath_resolved)
    state = initial_state
    advance(state, parameters, phases[0].control, 0, 0, step_us, breath_resolved)
    reference = state.conserved_oxygen_ml
    samples = [sample(0, state, parameters, reference)]
    current, phase_end = 0, 0
    for phase, duration in zip(phases, durations, strict=True):
        phase_end += duration
        while current < phase_end:
            boundary = min(phase_end, (current // sample_us + 1) * sample_us)
            state = advance(state, parameters, phase.control, current, boundary, step_us, breath_resolved)
            current = boundary
            samples.append(sample(current, state, parameters, reference))
    return samples

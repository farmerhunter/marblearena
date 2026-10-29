from __future__ import annotations

import argparse
import json
import math
import random
import struct
import wave
from array import array
from pathlib import Path


SAMPLE_RATE = 44100
TAU = 2.0 * math.pi


def pitch(note: float) -> float:
    return 440.0 * 2.0 ** ((note - 69.0) / 12.0)


def edge_fade(index: int, length: int, seconds: float = 0.012) -> float:
    remaining = (length - 1 - index) / (SAMPLE_RATE * seconds)
    return max(0.0, min(1.0, remaining))


def glass(note: float, duration: float, weight: float = 1.0) -> array:
    length = round(duration * SAMPLE_RATE)
    frequency = pitch(note)
    partials = ((1.0, 1.0), (2.004, 0.25), (2.756, 0.105), (4.071, 0.045))
    result = array("f")
    for index in range(length):
        time = index / SAMPLE_RATE
        attack = 1.0 - math.exp(-time / 0.0018)
        value = sum(
            amplitude * math.sin(TAU * frequency * ratio * time)
            * math.exp(-time / (duration * (0.29 if ratio == 1.0 else 0.13)))
            for ratio, amplitude in partials
            if frequency * ratio < 10000.0
        )
        result.append(value * attack * edge_fade(index, length) * weight)
    return result


def wood(note: float, duration: float, brightness: float = 1.0) -> array:
    length = round(duration * SAMPLE_RATE)
    frequency = pitch(note)
    rng = random.Random(round(note * 173.0))
    result = array("f")
    grain = 0.0
    for index in range(length):
        time = index / SAMPLE_RATE
        grain += 0.22 * (rng.uniform(-1.0, 1.0) - grain)
        value = math.sin(TAU * frequency * time) * math.exp(-time / (duration * 0.34))
        value += 0.19 * brightness * math.sin(TAU * frequency * 4.0 * time) * math.exp(-time / 0.038)
        value += 0.025 * brightness * math.sin(TAU * frequency * 9.8 * time) * math.exp(-time / 0.016)
        value += 0.12 * grain * math.exp(-time / 0.009)
        result.append(value * min(1.0, time / 0.0018) * edge_fade(index, length))
    return result


def pluck(note: int, duration: float, seed: int = 0) -> array:
    length = round(duration * SAMPLE_RATE)
    period = max(2, round(SAMPLE_RATE / pitch(note) - 0.5))
    rng = random.Random(239 + note * 83 + seed)
    string = [rng.uniform(-1.0, 1.0) for _ in range(period)]
    mean = sum(string) / period
    string = [(value - mean) * math.sin(math.pi * index / period) for index, value in enumerate(string)]
    mean = sum(string) / period
    string = [value - mean for value in string]
    result = array("f")
    for index in range(length):
        time = index / SAMPLE_RATE
        position = index % period
        value = string[position]
        string[position] = (value + string[(position + 1) % period]) * 0.497
        envelope = min(1.0, time / 0.004) * math.exp(-time / (duration * 0.55))
        result.append(value * envelope * edge_fade(index, length, 0.025))
    maximum = max(abs(value) for value in result) or 1.0
    return array("f", (value * 0.75 / maximum for value in result))


def bass(note: int, duration: float) -> array:
    source = pluck(note, duration)
    result = array("f")
    low = 0.0
    for index, value in enumerate(source):
        time = index / SAMPLE_RATE
        low += 0.055 * (value - low)
        body = math.sin(TAU * pitch(note) * time) * math.exp(-time / 0.18)
        result.append((low * 1.8 + body * 0.14) * min(1.0, time / 0.007))
    return result


def hand_drum() -> array:
    length = round(0.18 * SAMPLE_RATE)
    result = array("f")
    for index in range(length):
        time = index / SAMPLE_RATE
        phase = TAU * (118.0 * time + 28.0 * 0.018 * (1.0 - math.exp(-time / 0.018)))
        value = math.sin(phase) * math.exp(-time / 0.044)
        value += 0.13 * math.sin(TAU * 330.0 * time) * math.exp(-time / 0.024)
        result.append(value * min(1.0, time / 0.002) * edge_fade(index, length))
    return result


def percussion(duration: float, seed: int, kind: str) -> array:
    rng = random.Random(seed)
    length = round(duration * SAMPLE_RATE)
    result = array("f")
    low = 0.0
    for index in range(length):
        time = index / SAMPLE_RATE
        white = rng.uniform(-1.0, 1.0)
        low += 0.17 * (white - low)
        value = white - low
        if kind == "brush":
            value = 0.6 * low + 0.06 * value
            envelope = math.exp(-time / 0.045)
        else:
            value *= 0.18
            envelope = math.exp(-time / 0.021)
        result.append(value * envelope * min(1.0, time / 0.003) * edge_fade(index, length))
    return result


def spring(duration: float) -> array:
    length = round(duration * SAMPLE_RATE)
    result = array("f")
    for index in range(length):
        time = index / SAMPLE_RATE
        phase = TAU * (135.0 * time + 300.0 * (time - 0.12 * (1.0 - math.exp(-time / 0.12))))
        phase += 1.7 * math.sin(TAU * 19.0 * time) * math.exp(-time / 0.14)
        value = math.sin(phase) + 0.2 * math.sin(phase * 2.0) + 0.04 * math.sin(phase * 3.0)
        envelope = min(1.0, time / 0.004) * math.exp(-time / 0.16)
        result.append(value * envelope * edge_fade(index, length))
    return result


def periodic_noise(duration: float, seed: int, kind: str) -> array:
    rng = random.Random(seed)
    length = round(duration * SAMPLE_RATE)
    white = array("f", (rng.uniform(-1.0, 1.0) for _ in range(length)))
    result = array("f")
    low = 0.0
    lower = 0.0
    cutoff = 0.065 if kind == "rolling" else 0.018
    for cycle in range(2):
        for index, sample in enumerate(white):
            low += cutoff * (sample - low)
            lower += cutoff * 0.18 * (low - lower)
            if cycle == 1:
                position = index / length
                swell = 0.84 + 0.1 * math.sin(TAU * position) + 0.06 * math.cos(TAU * position * 3.0)
                value = (low - lower) * swell
                if kind == "rolling":
                    value += 0.016 * math.sin(TAU * 137.0 * duration * position)
                    value += 0.007 * math.sin(TAU * 283.0 * duration * position)
                result.append(value)
    return result


class Mix:
    def __init__(self, duration: float, looping: bool = False):
        self.length = round(duration * SAMPLE_RATE)
        self.left = array("f", [0.0]) * self.length
        self.right = array("f", [0.0]) * self.length
        self.looping = looping

    def add(self, signal: array, start: float = 0.0, gain: float = 1.0, pan: float = 0.0):
        offset = round(start * SAMPLE_RATE)
        angle = (pan + 1.0) * math.pi * 0.25
        left_gain = gain * math.cos(angle)
        right_gain = gain * math.sin(angle)
        for index, sample in enumerate(signal):
            target = offset + index
            if self.looping:
                target %= self.length
            elif target < 0 or target >= self.length:
                continue
            self.left[target] += sample * left_gain
            self.right[target] += sample * right_gain

    def room(self, signal: array, start: float, gain: float, pan: float):
        self.add(signal, start, gain, pan)
        self.add(signal, start + 0.093, gain * 0.12, -pan * 0.8)
        self.add(signal, start + 0.187, gain * 0.065, pan * 0.4)

    def save(self, path: Path, peak_target: float) -> dict:
        peak = max(max(abs(value) for value in self.left), max(abs(value) for value in self.right), 0.00001)
        gain = peak_target / peak
        frames = bytearray(self.length * 4)
        energy = 0.0
        total = 0.0
        for index, (left, right) in enumerate(zip(self.left, self.right)):
            left *= gain
            right *= gain
            energy += left * left + right * right
            total += left + right
            struct.pack_into("<hh", frames, index * 4, round(left * 32767.0), round(right * 32767.0))
        with wave.open(str(path), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(SAMPLE_RATE)
            output.writeframes(frames)
        boundary = max(abs(self.left[-1] - self.left[0]), abs(self.right[-1] - self.right[0])) * gain
        result = {
            "file": path.name,
            "seconds": round(self.length / SAMPLE_RATE, 3),
            "peak_dbfs": round(20.0 * math.log10(peak_target), 2),
            "rms_dbfs": round(20.0 * math.log10(math.sqrt(energy / (2.0 * self.length))), 2),
            "dc": round(total / (2.0 * self.length), 7),
            "looping": self.looping,
            "boundary_step": round(boundary, 6),
        }
        if peak_target >= 0.95:
            raise ValueError("Audio headroom must remain below -0.45 dBFS")
        if self.looping and boundary > 0.025:
            raise ValueError(f"Excessive loop boundary in {path.name}: {boundary}")
        return result


def music() -> Mix:
    beat = 60.0 / 112.0
    mix = Mix(beat * 64.0, looping=True)
    drum = hand_drum()
    shaker = percussion(0.085, 80, "shaker")
    brush = percussion(0.15, 81, "brush")
    chords = ((55, 59, 62, 64), (52, 55, 59, 62), (48, 55, 59, 64), (50, 57, 60, 64))
    roots = (43, 40, 36, 38)
    melodies = (
        ((0.0, 74), (0.75, 71), (1.5, 67), (3.0, 69)),
        ((0.0, 71), (1.5, 74), (2.25, 76), (3.25, 74)),
        ((0.0, 71), (0.75, 67), (1.5, 64), (3.0, 67)),
        ((0.0, 69), (1.0, 71), (2.5, 67)),
        ((0.0, 72), (0.75, 71), (1.5, 67), (3.0, 64)),
        ((0.0, 67), (1.5, 71), (2.25, 74), (3.25, 76)),
        ((0.0, 74), (0.75, 72), (1.5, 69), (3.0, 66)),
        ((0.0, 69), (1.0, 72), (2.0, 74), (2.75, 71), (3.5, 69)),
    )
    wood_cache = {note: wood(note, 0.68, 0.75) for note in range(60, 89)}
    strings = {note: pluck(note, 0.58) for note in range(48, 77)}
    bass_cache = {note: bass(note, 0.52) for note in range(36, 56)}
    for bar in range(16):
        chord_index = (bar // 2) % 4
        bar_start = bar * beat * 4.0
        root = roots[chord_index]
        mix.add(drum, bar_start, 0.065)
        mix.add(drum, bar_start + beat * 2.5, 0.038)
        for pulse in (1.0, 3.0):
            mix.add(brush, bar_start + beat * pulse + 0.012, 0.16, 0.22)
        for eighth in range(8):
            if bar % 4 == 3 and eighth >= 6:
                continue
            swing = 0.055 if eighth % 2 else 0.0
            mix.add(shaker, bar_start + beat * (eighth * 0.5 + swing), 0.09 if eighth % 2 else 0.05, -0.3)
        for pulse, interval, gain in ((0.0, 0, 0.16), (1.5, 7, 0.1), (2.75, 12, 0.105)):
            mix.add(bass_cache[root + interval], bar_start + pulse * beat, gain)
        for strum_beat, gain in ((0.5, 0.043), (2.5, 0.036)):
            for voice, note in enumerate(chords[chord_index]):
                mix.add(strings[note], bar_start + strum_beat * beat + voice * 0.008, gain, -0.42 + voice * 0.09)
        for phrase_index, (pulse, note) in enumerate(melodies[bar % 8]):
            start = bar_start + pulse * beat
            gain = 0.16 if phrase_index == 0 else 0.13
            pan = 0.12 if phrase_index % 2 else -0.06
            mix.add(wood_cache[note], start, gain, pan)
            mix.add(wood_cache[note], start + 0.047, gain * 0.08, -pan)
            mix.add(wood_cache[note], start + 0.109, gain * 0.035, 0.32)
        if bar >= 8 and bar % 2:
            for pulse, note in ((0.75, chords[chord_index][2] + 12), (2.25, chords[chord_index][-1] + 12)):
                mix.add(wood_cache[note], bar_start + pulse * beat, 0.038, 0.48)
    return mix


def generate(output: Path) -> list[dict]:
    output.mkdir(parents=True, exist_ok=True)
    results = []
    for number, note in enumerate((85.0, 88.0, 91.0), 1):
        mix = Mix(0.4)
        mix.room(glass(note, 0.21), 0.0, 1.0, (number - 2) * 0.1)
        results.append(mix.save(output / f"glass_tap_{number}.wav", 0.44))
    for number, note in enumerate((75.0, 79.0), 1):
        mix = Mix(0.78)
        mix.room(glass(note, 0.58), 0.0, 1.0, 0.0)
        mix.add(glass(note + 7, 0.16), 0.003, 0.25, 0.1)
        results.append(mix.save(output / f"glass_hit_{number}.wav", 0.62))
    mix = Mix(0.65)
    mix.add(spring(0.48), 0.0, 0.55)
    for index, note in enumerate((67.0, 71.0, 74.0)):
        mix.add(wood(note, 0.27), 0.15 + index * 0.055, 0.28, (index - 1) * 0.2)
    results.append(mix.save(output / "pulse.wav", 0.5))
    mix = Mix(0.42)
    mix.add(wood(79.0, 0.2, 0.65), 0.0, 0.8, -0.1)
    mix.add(glass(91.0, 0.12), 0.018, 0.12, 0.1)
    results.append(mix.save(output / "select.wav", 0.36))
    mix = Mix(0.42)
    mix.add(wood(74.0, 0.25), 0.0, 0.8)
    mix.add(glass(86.0, 0.18), 0.0, 0.16)
    results.append(mix.save(output / "countdown.wav", 0.46))
    mix = Mix(1.0)
    for index, note in enumerate((67.0, 71.0, 74.0, 79.0)):
        mix.room(wood(note, 0.5), index * 0.07, 0.65, (index - 1.5) * 0.2)
    mix.add(pluck(55, 0.6), 0.0, 0.3)
    results.append(mix.save(output / "race_start.wav", 0.56))
    mix = Mix(2.6)
    for index, note in enumerate((67.0, 71.0, 74.0, 79.0, 76.0, 79.0)):
        mix.room(wood(note, 0.85), index * 0.19, 0.52 if index < 5 else 0.72, (index % 3 - 1) * 0.25)
    for index, note in enumerate((55, 59, 62, 64)):
        mix.add(pluck(note, 1.1), 0.4 + index * 0.012, 0.24, (index - 1.5) * 0.3)
    results.append(mix.save(output / "victory.wav", 0.58))
    for name, duration, kind, target in (("rolling_loop.wav", 2.0, "rolling", 0.19), ("wind_loop.wav", 4.0, "wind", 0.13)):
        mix = Mix(duration, looping=True)
        mix.add(periodic_noise(duration, 131, kind), gain=0.8, pan=-0.45)
        mix.add(periodic_noise(duration, 195, kind), gain=0.8, pan=0.45)
        results.append(mix.save(output / name, target))
    results.append(music().save(output / "race_music.wav", 0.52))
    return results


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "Assets" / "Resources" / "Audio")
    args = parser.parse_args()
    for result in generate(args.output):
        print(json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()

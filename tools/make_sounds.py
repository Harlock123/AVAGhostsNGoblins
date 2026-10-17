#!/usr/bin/env python3
"""Synthesises AVAGhostsNGoblins's sound effects and tunes into ../src/AVAGhostsNGoblins/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGhostsNGoblins", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out



def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Sir Hollin.
    write("Jump.wav", tone(0.12, 300, 520, "square", attack=0.002, release=1.2, gain=0.5), 0.14)
    write("Throw.wav", tone(0.1, 1400, 500, "noise", attack=0.002, release=1.4, gain=0.6), 0.18)
    write("ArmourOff.wav", cat(mix(tone(0.12, 1600, 600, "square", release=1.0, gain=0.5), tone(0.1, 3000, 800, "noise", release=1.2, gain=0.4)),
                               *[tone(0.05, 900 - i * 120, kind="square", release=0.6, gain=0.4) for i in range(5)]), 0.34)
    write("Die.wav", cat(*[tone(0.14, hz(n), kind="pulse", release=0.5) for n in ["E5", "D#5", "D5", "C#5"]], tone(0.6, hz("C5"), kind="pulse", vibrato=0.03, release=0.6)), 0.38)
    write("Armour.wav", cat(*[tone(0.06, hz(n), kind="square", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6"]]), 0.26)
    write("Pickup.wav", cat(tone(0.06, hz("G5"), kind="pulse", release=0.4), tone(0.1, hz("D6"), kind="pulse", release=0.5)), 0.24)
    write("Pot.wav", mix(tone(0.15, 2200, 600, "noise", attack=0.001, release=1.2), tone(0.12, 500, 260, "square", release=1.0, gain=0.4)), 0.3)
    write("Key.wav", cat(*[tone(0.08, hz(n), kind="sine", release=0.4) for n in ["C6", "G6", "C7", "E7"]], tone(0.4, hz("G7"), kind="sine", vibrato=0.01, release=0.6)), 0.3)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)
    write("Bonus.wav", tone(0.03, hz("A6"), kind="square", attack=0.001, release=0.8, gain=0.5), 0.14)
    # Hitting things.
    write("Hit.wav", mix(tone(0.08, 900, 300, "square", attack=0.001, release=1.2, gain=0.5), tone(0.06, 2500, 800, "noise", release=1.5, gain=0.4)), 0.26)
    write("Kill.wav", mix(tone(0.3, 1800, 200, "noise", attack=0.001, release=0.9), tone(0.25, 400, 80, "square", release=0.8, gain=0.5)), 0.32)
    write("Flame.wav", tone(0.5, 600, 300, "noise", attack=0.02, release=0.5, gain=0.6), 0.2)
    # Creatures.
    write("Rise.wav", mix(tone(0.6, 120, 90, "saw", attack=0.1, vibrato=0.08, vib_rate=6, release=0.5), tone(0.5, 400, 200, "noise", attack=0.1, release=0.7, gain=0.4)), 0.3)
    write("Caw.wav", cat(tone(0.12, 900, 700, "saw", vibrato=0.1, vib_rate=40, release=0.6), silence(0.05), tone(0.14, 850, 650, "saw", vibrato=0.1, vib_rate=40, release=0.6)), 0.2)
    write("Spit.wav", tone(0.18, 300, 900, "square", attack=0.005, vibrato=0.06, vib_rate=30, release=0.8, gain=0.5), 0.2)
    write("Swoop.wav", tone(0.4, 1800, 400, "sine", vibrato=0.05, vib_rate=12, release=0.5), 0.2)
    write("Thud.wav", mix(tone(0.25, 90, 40, "sine", attack=0.002, release=1.0), tone(0.15, 600, 150, "noise", release=1.2, gain=0.6)), 0.42)
    write("BossCome.wav", cat(*[tone(0.18, hz(n), kind="saw", vibrato=0.02, release=0.4) for n in ["C3", "C3", "D#3", "C3", "F#3"]], tone(0.6, hz("F3"), kind="saw", vibrato=0.03, release=0.5)), 0.36)
    write("BossHit.wav", mix(tone(0.12, 300, 120, "square", attack=0.001, release=1.0, gain=0.6), tone(0.1, 1800, 400, "noise", release=1.3, gain=0.4)), 0.3)
    write("BossDie.wav", mix(tone(1.6, 2600, 80, "noise", attack=0.002, release=0.4), tone(1.4, 160, 30, "saw", vibrato=0.25, vib_rate=8, release=0.5, gain=0.7)), 0.5)

    # The stage march (an original composition): a galloping figure in C minor, a whole number of bars so it loops.
    gallop = lambda n: [(n, 0.5), (n, 0.25), (n, 0.25)]
    lead = [("C5", 1), ("D#5", 0.5), ("G5", 0.5), ("F5", 0.5), ("D#5", 0.5), ("D5", 1),
            ("C5", 0.5), ("D5", 0.5), ("D#5", 1), ("F5", 0.5), ("D#5", 0.5), ("D5", 1),
            ("G5", 1), ("G#5", 0.5), ("G5", 0.5), ("F5", 0.5), ("D#5", 0.5), ("F5", 1),
            ("D#5", 0.5), ("D5", 0.5), ("C5", 0.5), ("B4", 0.5), ("C5", 2)]
    bass = gallop("C3") * 4 + gallop("G#2") * 2 + gallop("G2") * 2 + gallop("C3") * 2 + gallop("F2") * 2 + gallop("G2") * 2 + gallop("C3") * 2
    write("Music.wav", tune(150, lead, bass, kind="pulse"), 0.26)
    title_lead = [("C5", 1.5), ("G4", 0.5), ("C5", 1), ("D#5", 1), ("D5", 1.5), ("C5", 0.5), ("B4", 1), ("G4", 1),
                  ("C5", 1.5), ("D#5", 0.5), ("G5", 1), ("F5", 1), ("D#5", 1), ("D5", 1), ("C5", 2)]
    title_bass = [("C3", 2), ("G#2", 2), ("G2", 2), ("G2", 2), ("C3", 2), ("F2", 2), ("G2", 2), ("C3", 2)]
    write("TitleTune.wav", cat(tune(96, title_lead, title_bass, kind="pulse"), silence(0.8)), 0.38)
    write("StageClear.wav", tune(150, [("G4", 0.5), ("C5", 0.5), ("D#5", 0.5), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.36)
    write("Victory.wav", tune(120, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 3)],
                              [("C3", 1), ("G2", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("C3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("G4", 1), ("D#4", 1), ("D4", 1), ("B3", 1), ("C4", 4)], [("C3", 2), ("G#2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()

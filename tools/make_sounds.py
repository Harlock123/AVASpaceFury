#!/usr/bin/env python3
"""Synthesises AVASpaceFury's sound effects and tunes into ../src/AVASpaceFury/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASpaceFury", "Sounds")
RNG = random.Random(1982)


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




def robot(seconds, syllables, base=95):
    """The Commander's voice: a buzzy, robotic warble, its pitch and 'mouth' jumping syllable by syllable."""
    n = int(RATE * seconds)
    rng = random.Random(7)
    pitches = [base * rng.choice([1, 1.12, 1.26, 0.89, 1.5]) for _ in range(syllables)]
    out, phase = [], 0.0
    for i in range(n):
        t = i / RATE
        k = min(syllables - 1, int(t / seconds * syllables))
        within = (t / seconds * syllables) % 1
        f = pitches[k] * (1 + 0.04 * math.sin(2 * math.pi * 6 * t))
        phase += f / RATE
        saw = 2 * (phase % 1) - 1
        # A crude formant: the buzz ring-modulated by a vowel tone that moves each syllable.
        vowel = 500 + 700 * ((k * 0.37) % 1)
        env = math.sin(math.pi * within) ** 0.6
        out.append(env * (0.6 * saw + 0.4 * saw * math.sin(2 * math.pi * vowel * t)))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's gun: a bright zap; the enemy's, a low blat; the thruster, a soft roar.
    write("Fire.wav", tone(0.12, 2400, 700, "square", release=1.0), 0.22)
    write("EnemyFire.wav", tone(0.14, 500, 260, "saw", release=0.8), 0.2)
    write("Thrust.wav", mix(tone(0.28, 600, 400, "noise", attack=0.03, release=0.5), tone(0.28, 70, 60, "saw", release=0.5, gain=0.4)), 0.22)
    # Hits: a fragment pings apart, a Raider bursts, a Marauder clangs.
    write("FragmentHit.wav", mix(tone(0.15, 3000, 800, "noise", release=0.9), tone(0.12, 1800, 2800, "tri", release=0.7, gain=0.6)), 0.32)
    write("RaiderHit.wav", mix(tone(0.4, 2800, 120, "noise", release=0.7), tone(0.35, 600, 90, "square", release=0.7, gain=0.6)), 0.42)
    write("MarauderHit.wav", tone(0.12, 900, 850, "square", release=0.6, vibrato=0.05, vib_rate=40), 0.3)
    # The enemy building itself: pieces clicking together, and two Raiders fusing.
    write("Assemble.wav", cat(*[tone(0.04, f, kind="square", release=0.6) for f in [700, 900, 1200]], tone(0.2, 1500, 1800, "tri", release=0.5)), 0.3)
    write("Merge.wav", mix(tone(0.6, 200, 900, "saw", release=0.5, vibrato=0.1, vib_rate=20), tone(0.6, 300, 1200, "square", release=0.5, gain=0.4)), 0.34)
    write("Lone.wav", tone(0.4, 1200, 1800, "tri", release=0.5, vibrato=0.15, vib_rate=16), 0.24)
    write("Death.wav", cat(mix(tone(0.9, 3500, 90, "noise", release=0.6), tone(0.9, 420, 40, "saw", release=0.6, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["C5", "A4", "F4", "C4"]]), 0.5)
    write("Taunt.wav", robot(2.0, 9, base=80), 0.32)
    # Docking: the modules arriving, and the clunk-and-chime of docking with one.
    write("ModulesIn.wav", cat(*[tone(0.1, hz(n), kind="tri", release=0.5) for n in ["C5", "G5", "C6"]]), 0.34)
    write("Docked.wav", cat(mix(tone(0.12, 160, 80, "square", release=0.8), tone(0.08, 1500, 600, "noise", release=0.8, gain=0.4)),
                            *[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6"]]), 0.4)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["G4", "C5", "E5"]]), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "G5", "C6"]]), 0.4)

    # Space: a low, slowly beating hum. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        hum.append(math.sin(2 * math.pi * 55 * t) * (0.5 + 0.2 * math.sin(2 * math.pi * 0.5 * t)) + 0.25 * math.sin(2 * math.pi * 55.5 * t)
                   + 0.04 * math.sin(2 * math.pi * (880 + 30 * math.sin(2 * math.pi * 0.25 * t)) * t))
    write("Music.wav", hum, 0.26)

    # The title: a driving space anthem in C minor (an original tune).
    lead = [("C5", 1), ("Eb5", 0.5), ("G5", 0.5), ("C6", 1), ("Bb5", 1), ("Ab5", 1), ("G5", 0.5), ("F5", 0.5), ("G5", 2),
            ("F5", 1), ("Ab5", 0.5), ("C6", 0.5), ("Eb6", 1), ("D6", 1), ("C6", 1), ("Bb5", 0.5), ("Ab5", 0.5), ("G5", 2),
            ("Ab5", 1), ("G5", 1), ("F5", 1), ("Eb5", 1), ("D5", 1), ("Eb5", 0.5), ("F5", 0.5), ("G5", 1), ("B4", 1), ("C5", 4)]
    bass = [("C3", 1), ("C3", 1), ("G2", 1), ("G2", 1), ("Ab2", 1), ("Ab2", 1), ("G2", 2),
            ("F2", 1), ("F2", 1), ("Ab2", 1), ("Ab2", 1), ("Eb2", 1), ("Eb2", 1), ("G2", 2),
            ("F2", 2), ("Ab2", 2), ("G2", 2), ("G2", 2), ("C3", 4)]
    write("TitleTune.wav", cat(tune(170, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("Eb5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("G4", 1), ("Eb4", 1), ("D4", 1), ("B3", 1), ("C4", 4)], [("C3", 2), ("Ab2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()

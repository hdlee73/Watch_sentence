#!/usr/bin/env python3
"""Build src/WatchSentence/quotes.tsv from public-domain Project Gutenberg texts.

Usage: python3 tools/build_quotes.py <dir-of-gutenberg-txt-files> <out.tsv>

Each output row: HH:MM <TAB> quote <TAB> time-phrase <TAB> title <TAB> author
The time phrase is the exact substring of the quote that names the time, so the
app can emphasise it. A quote with no am/pm hint is emitted for both halves of
the day.
"""
import glob
import os
import re
import sys
from collections import defaultdict

NUM = {
    "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7,
    "eight": 8, "nine": 9, "ten": 10, "eleven": 11, "twelve": 12,
    "thirteen": 13, "fourteen": 14, "fifteen": 15, "sixteen": 16,
    "seventeen": 17, "eighteen": 18, "nineteen": 19, "twenty": 20,
    "twenty-one": 21, "twenty-two": 22, "twenty-three": 23, "twenty-four": 24,
    "twenty-five": 25, "twenty-six": 26, "twenty-seven": 27,
    "twenty-eight": 28, "twenty-nine": 29, "five-and-twenty": 25,
    "a quarter": 15, "quarter": 15, "half": 30,
}
HOUR_WORDS = "one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve"
HOUR = rf"(?:{HOUR_WORDS}|1[0-2]|[1-9])"
MIN_WORDS = (r"twenty[- ](?:one|two|three|four|five|six|seven|eight|nine)|five-and-twenty|"
             r"thirteen|fourteen|sixteen|seventeen|eighteen|nineteen|"
             r"one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty|"
             r"[1-9]|[1-5][0-9]")
AMPM = r"(?:\s*(?P<ampm>a\.\s?m\.|p\.\s?m\.|A\.\s?M\.|P\.\s?M\.|in the (?:morning|afternoon|evening)|at night))?"

PATTERNS = [
    # half past four / a quarter to six / twenty minutes past ten
    re.compile(rf"(?<![-\w])(?P<min>half|(?:a )?quarter|{MIN_WORDS})(?: minutes?)? (?P<rel>past|after|to|before|of) (?P<hour>{HOUR})(?: o'clock)?{AMPM}\b(?!\s*(?:years|minutes|hours|days|o'clock|and|-|per))", re.I),
    # four o'clock / 11 o'clock
    re.compile(rf"\b(?P<hour>{HOUR}) o'clock{AMPM}", re.I),
    # 4.30 p.m. / 10:15 a.m.
    re.compile(rf"\b(?P<hour>1[0-2]|0?[1-9])[.:](?P<dmin>[0-5][0-9])\s*(?P<ampm>a\.\s?m\.|p\.\s?m\.|A\.\s?M\.|P\.\s?M\.)"),
    # at 4.30 / by 10:15 (no am/pm, needs a preposition before it)
    re.compile(rf"\b(?:at|by|till|until|about|after|before|past) (?P<hour>1[0-2]|0?[1-9])[.:](?P<dmin>[0-5][0-9])\b(?![.:]?\d)"),
    # three-thirty / ten-fifteen
    re.compile(rf"\b(?P<hour>{HOUR_WORDS})[- ](?P<wmin>thirty|fifteen|forty-five|forty|twenty|ten|fifty)(?: (?P<ampm2>a\.m\.|p\.m\.|in the (?:morning|afternoon|evening)))\b", re.I),
    # noon / midnight
    re.compile(r"\b(?P<special>midnight|mid-night|noon|midday|mid-day)\b", re.I),
]

MORNING = re.compile(r"\b(morning|a\.\s?m\.|breakfast|dawn|sunrise|daybreak)\b", re.I)
EVENING = re.compile(r"\b(evening|afternoon|night|p\.\s?m\.|dinner|supper|sunset|dusk|tea)\b", re.I)
CLOCK = re.compile(r"\b(clock|watch|struck|strike|striking|train|morning|evening|night|afternoon|minutes?|breakfast|dinner|supper|o'clock|late|early)\b", re.I)
WORDMIN = {"fifteen": 15, "thirty": 30, "forty-five": 45, "forty": 40, "twenty": 20, "ten": 10, "fifty": 50}


def hour_val(s):
    s = s.lower()
    return int(s) if s.isdigit() else NUM[s]


def parse(m):
    g = m.groupdict()
    if g.get("special"):
        sp = g["special"].lower().replace("-", "")
        return [(0, 0)] if sp == "midnight" else [(12, 0)], True
    h = hour_val(g["hour"])
    if not 1 <= h <= 12:
        return None, False
    minute = 0
    if g.get("min"):
        mm = g["min"].lower().replace(" ", "-") if g["min"].lower().startswith("twenty") else g["min"].lower()
        mm = mm.replace("a-quarter", "a quarter")
        if mm.isdigit():
            mv = int(mm)
        else:
            mv = NUM.get(mm)
        if mv is None or mv >= 60:
            return None, False
        bare = "minute" not in m.group(0).lower() and mm not in ("half", "quarter", "a quarter")
        if bare and (g["rel"].lower() not in ("past", "to") or mv % 5 or not CLOCK.search(m.string)):
            return None, False
        # "ten to one" (odds), "from ten to twelve" (ranges)
        if bare and g["rel"].lower() == "to" and (h == 1 or re.search(r"\b(from|between|or)\s*$", m.string[:m.start()], re.I)):
            return None, False
        if g["rel"].lower() in ("to", "before", "of"):
            if mv == 30:
                return None, False
            minute = 60 - mv
            h = h - 1 if h > 1 else 12
        else:
            minute = mv
    elif g.get("dmin"):
        minute = int(g["dmin"])
    elif g.get("wmin"):
        minute = WORDMIN[g["wmin"].lower()]
    ampm = (g.get("ampm") or g.get("ampm2") or "").lower()
    return [(h, minute)], ampm


def to24(h12, minute, half):
    h = h12 % 12
    return (h + (12 if half == "pm" else 0), minute)


START = re.compile(r"\*\*\* ?START OF (THE|THIS) PROJECT GUTENBERG.*", re.I)
END = re.compile(r"\*\*\* ?END OF (THE|THIS) PROJECT GUTENBERG.*|End of (the )?Project Gutenberg", re.I)


def load(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        raw = f.read()
    raw = raw.replace("\r\n", "\n").lstrip("﻿")
    title = re.search(r"^Title:\s*(.+(?:\n {2,}.+)*)", raw, re.M)
    author = re.search(r"^Author:\s*(.+)", raw, re.M)
    title = re.sub(r"\s+", " ", title.group(1)).strip() if title else None
    author = author.group(1).strip() if author else None
    s = START.search(raw)
    e = END.search(raw, s.end() if s else 0)
    body = raw[s.end() if s else 0: e.start() if e else len(raw)]
    return title, author, body


SENT_SPLIT = re.compile(r"(?:(?<=[.!?])|(?<=[.!?][\"'”’)]))\s+(?=[\"'“‘(]?[A-Z])")
ABBR = re.compile(r"\b(Mr|Mrs|Dr|St|Mme|Mlle|Messrs|Capt|Col|Gen|Rev|Prof|No|M)\.$")


def sentences(para):
    parts = SENT_SPLIT.split(para)
    out = []
    for p in parts:
        if out and ABBR.search(out[-1]):
            out[-1] += " " + p
        else:
            out.append(p)
    return out


def clean(t):
    t = re.sub(r"_([^_]+)_", r"\1", t)
    t = t.replace("--", "—")
    t = re.sub(r"\s+", " ", t).strip()
    return t


def trim(sentence, start, end, max_words=32):
    words = sentence.split()
    if len(words) <= max_words:
        return sentence
    # cut on clause boundaries around the phrase
    left = sentence[:start]
    right = sentence[end:]
    lcuts = [m.end() for m in re.finditer(r"[;:—]\s|,\s", left)]
    rcuts = [m.start() + 1 for m in re.finditer(r"[;:—,]\s", right)]
    best = None
    for lc in [0] + lcuts[::-1]:
        for rc in rcuts + [len(right)]:
            frag = sentence[lc:end + rc]
            n = len(frag.split())
            if 6 <= n <= max_words:
                if best is None or n > len(best[2].split()):
                    best = (lc, end + rc, frag)
                break
    if not best:
        return None
    lc, rc, frag = best
    frag = frag.strip().rstrip(",;:—")
    if lc > 0:
        frag = "… " + frag[0].lower() + frag[1:] if frag[:1].isupper() and not frag.startswith("I ") else "… " + frag
    if rc < len(sentence):
        frag = frag + " …"
    return frag


BAD = re.compile(r"(CHAPTER|Chapter [IVXL]+|\[|\]|http|Gutenberg|www\.|\bpage\b|\b1[0-9]{3}\b)")


def main(src, out):
    found = defaultdict(list)
    for path in sorted(glob.glob(os.path.join(src, "*.txt"))):
        title, author, body = load(path)
        if not title or not author:
            continue
        for para in re.split(r"\n\s*\n", body):
            para = clean(para)
            if len(para) < 20:
                continue
            for sent in sentences(para):
                if BAD.search(sent):
                    continue
                taken = []
                for pat in PATTERNS:
                    for m in pat.finditer(sent):
                        if any(m.start() < b and a < m.end() for a, b in taken):
                            continue
                        times, ampm = parse(m)
                        if not times:
                            continue
                        taken.append((m.start(), m.end()))
                        quote = trim(sent, m.start(), m.end())
                        if not quote or len(quote.split()) < 6:
                            continue
                        phrase = m.group(0).strip()
                        if phrase not in quote:
                            continue
                        if m.groupdict().get("special"):
                            halves = ["x"]
                        elif ampm:
                            halves = ["am"] if re.match(r"a\.", ampm) or "morning" in ampm else ["pm"]
                        elif MORNING.search(sent) and not EVENING.search(sent):
                            halves = ["am"]
                        elif EVENING.search(sent) and not MORNING.search(sent):
                            halves = ["pm"]
                        else:
                            halves = ["am", "pm"]
                        for h12, mi in times:
                            hv = halves
                            if h12 == 12 and not ampm and halves != ["x"]:
                                hv = ["pm"] if re.search(r"\bnight\b", sent, re.I) else ["am", "pm"]
                            for half in hv:
                                hh, mm = (h12, mi) if half == "x" else to24(h12, mi, half)
                                if h12 == 12 and half == "pm" and not ampm and re.search(r"\bnight\b", sent, re.I):
                                    hh = 0
                                found[(hh, mm)].append((quote, phrase, title, author))
    rows = []
    for key in sorted(found):
        seen = set()
        per_book = defaultdict(int)
        cands = sorted(found[key], key=lambda q: (abs(len(q[0].split()) - 20), q[0]))
        for q in cands:
            if q[0] in seen or per_book[q[2]] >= 2:
                continue
            seen.add(q[0])
            per_book[q[2]] += 1
            rows.append((f"{key[0]:02d}:{key[1]:02d}",) + q)
            if len([r for r in rows if r[0] == f"{key[0]:02d}:{key[1]:02d}"]) >= 6:
                break
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("# time\tquote\tphrase\ttitle\tauthor — generated by tools/build_quotes.py from Project Gutenberg texts\n")
        for r in rows:
            f.write("\t".join(x.replace("\t", " ") for x in r) + "\n")
    minutes = {r[0] for r in rows}
    print(f"{len(rows)} quotes, {len(minutes)}/1440 minutes covered", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])

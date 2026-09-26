#!/usr/bin/env python3
"""Find, tap and probe on-screen elements by accessibility label, via idb.

Used by ios-smoke-test.sh. Every command needs --udid <simulator>.

  probe X Y               print what's at (X, Y) and how many elements
                          describe-all returns; exit 1 if idb can't answer
  find LABEL [LABEL ...]  wait until any LABEL is on screen, then print
                          FOUND<TAB>label<TAB>x<TAB>y<TAB>method
                          (--tap also taps it; exit 1 on timeout, 3 as soon
                          as the app stops answering hit-tests)
  gone LABEL --at X Y     wait until (X, Y) no longer shows LABEL
                          (exit 1 if it's still there, 2 if the app stopped
                          answering hit-tests)
  responsive              exit 0 if the app answers a hit-test in time
  tap X Y
  swipe X1 Y1 X2 Y2

There are two ways to find an element. `idb ui describe-all` returns every
element and its frame in one call, but on some iOS 26 simulators it returns
only the root node, so fewer than two elements counts as "not working" and
the fallback is a scan: `idb ui describe-point` over a grid of points, which
only needs single-point hit-testing. Slow, but it doesn't depend on the part
that's reported broken.

Labels match exactly and case-sensitively, so "Currently playing: ..." is
never mistaken for the PLAY button.
"""
import argparse
import json
import re
import subprocess
import sys
import time

UDID = None
CALL_TIMEOUT = 20
# A hit-test the app doesn't answer within this long means its main thread
# is stuck. Short on purpose: a scan makes a hundred-odd of these calls.
HIT_TEST_TIMEOUT = 10


class Unresponsive(Exception):
    """The app stopped answering hit-tests - it's frozen."""


def idb(*args, timeout=CALL_TIMEOUT):
    # --udid goes after the subcommand: `idb --udid X ui ...` is rejected
    # (idb reads the UDID as the command name).
    cmd = ["idb", *args, "--udid", UDID]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, (p.stdout + p.stderr).strip()
    except subprocess.TimeoutExpired:
        return 124, "idb call timed out after %ss: %s" % (timeout, " ".join(cmd))


def parse_json(text):
    try:
        return json.loads(text)
    except ValueError:
        pass
    for line in text.splitlines():
        line = line.strip()
        if line[:1] in ("[", "{"):
            try:
                return json.loads(line)
            except ValueError:
                continue
    return None


def label_of(el):
    if isinstance(el, list) and len(el) == 1:
        el = el[0]
    if not isinstance(el, dict):
        return None
    return el.get("AXLabel") or el.get("label") or None


def center_of(el):
    frame = el.get("frame")
    if isinstance(frame, dict) and all(k in frame for k in ("x", "y", "width", "height")):
        return round(frame["x"] + frame["width"] / 2), round(frame["y"] + frame["height"] / 2)
    text = el.get("AXFrame") or (frame if isinstance(frame, str) else None)
    if text:
        nums = [float(n) for n in re.findall(r"-?\d+(?:\.\d+)?", text)]
        if len(nums) == 4:
            return round(nums[0] + nums[2] / 2), round(nums[1] + nums[3] / 2)
    return None


def describe_all():
    """Every element on screen, or None when describe-all isn't usable."""
    rc, out = idb("ui", "describe-all")
    if rc != 0:
        return None
    data = parse_json(out)
    if isinstance(data, list) and len(data) >= 2:
        return data
    return None


def label_at(x, y, timeout=HIT_TEST_TIMEOUT):
    """(label or None, raw output, idb exit code) for the element at (x, y).
    Raises Unresponsive if the app doesn't answer at all."""
    rc, out = idb("ui", "describe-point", str(x), str(y), timeout=timeout)
    if rc == 124:
        raise Unresponsive("no answer to a hit-test at (%s, %s) within %ss" % (x, y, timeout))
    data = parse_json(out)
    if data is not None:
        return label_of(data), out, rc
    m = re.search(r'"AXLabel"\s*:\s*"([^"]*)"', out)
    return (m.group(1) if m else None), out, rc


def find_once(labels, xs, ys, seen):
    elements = describe_all()
    if elements is not None:
        for el in elements:
            lbl = label_of(el)
            if lbl:
                seen.add(lbl)
            if lbl in labels:
                c = center_of(el)
                if c:
                    return lbl, c[0], c[1], "describe-all"
        return None
    for y in ys:
        for x in xs:
            lbl, _, _ = label_at(x, y)
            if lbl:
                seen.add(lbl)
            if lbl in labels:
                cx, cy = centre_by_probing(lbl, x, y)
                return lbl, cx, cy, "scan"
    return None


def edge(label, x, y, dx, dy, limit):
    """How far from (x, y), in steps of (dx, dy), `label` is still what's hit."""
    last = 0
    for i in range(1, limit + 1):
        lbl, _, _ = label_at(x + dx * i, y + dy * i)
        if lbl != label:
            break
        last = i
    return last


def centre_by_probing(label, x, y):
    """A scan hits a button at whichever edge the grid reached first, and a
    tap right on an edge can miss. Walk out to the edges and use the middle."""
    step = 4
    up = edge(label, x, y, 0, -step, 20)
    down = edge(label, x, y, 0, step, 25)
    cy = y + (down - up) * step // 2
    left = edge(label, x, cy, -step, 0, 50)
    right = edge(label, x, cy, step, 0, 50)
    cx = x + (right - left) * step // 2
    return cx, cy


def cmd_find(a):
    labels = set(a.labels)
    xs = [int(v) for v in a.xs.split(",")]
    ys = list(range(a.y0, a.y1 + 1, a.step))
    seen = set()
    deadline = time.time() + a.timeout
    while True:
        try:
            hit = find_once(labels, xs, ys, seen)
        except Unresponsive as e:
            print("FROZEN: the app %s" % e, file=sys.stderr)
            return 3
        if hit:
            lbl, x, y, method = hit
            print("FOUND\t%s\t%d\t%d\t%s" % (lbl, x, y, method))
            if a.tap:
                rc, out = idb("ui", "tap", str(x), str(y))
                if rc != 0:
                    print("tap at (%d, %d) failed: %s" % (x, y, out), file=sys.stderr)
                    return 1
            return 0
        if time.time() >= deadline:
            break
        time.sleep(1)
    shown = sorted(seen)
    print("none of %s found within %ss (xs=%s, y=%d..%d). Labels seen: %s"
          % (sorted(labels), a.timeout, a.xs, a.y0, a.y1, shown[:40] if shown else "none"),
          file=sys.stderr)
    return 1


def cmd_gone(a):
    x, y = a.at
    deadline = time.time() + a.timeout
    while True:
        try:
            lbl, out, rc = label_at(x, y)
        except Unresponsive as e:
            print("FROZEN: the app %s" % e, file=sys.stderr)
            return 2
        if lbl != a.label:
            return 0
        if time.time() >= deadline:
            print("'%s' still at (%d, %d) after %ss" % (a.label, x, y, a.timeout), file=sys.stderr)
            return 1
        time.sleep(0.5)


def cmd_responsive(a):
    rc, out = idb("ui", "describe-point", "196", "400", timeout=a.timeout)
    if rc != 0:
        print(out, file=sys.stderr)
    return 0 if rc == 0 else 1


def cmd_probe(a):
    rc, out = idb("ui", "describe-point", str(a.x), str(a.y))
    print("describe-point %d %d (exit %d): %s" % (a.x, a.y, rc, out))
    elements = describe_all()
    print("describe-all: %s" % ("%d elements - fast lookups available" % len(elements)
                                if elements is not None else "unusable here - falling back to point scans"))
    return 0 if rc == 0 else 1


def cmd_tap(a):
    rc, out = idb("ui", "tap", str(a.x), str(a.y))
    if rc != 0:
        print(out, file=sys.stderr)
    return rc


def cmd_swipe(a):
    rc, out = idb("ui", "swipe", str(a.x1), str(a.y1), str(a.x2), str(a.y2))
    if rc != 0:
        print(out, file=sys.stderr)
    return rc


def main():
    global UDID
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--udid", required=True)
    sub = p.add_subparsers(dest="cmd", required=True)

    f = sub.add_parser("find")
    f.add_argument("labels", nargs="+")
    f.add_argument("--xs", default="196", help="comma-separated x positions to scan")
    f.add_argument("--y0", type=int, default=60)
    f.add_argument("--y1", type=int, default=830)
    f.add_argument("--step", type=int, default=20)
    f.add_argument("--timeout", type=float, default=10)
    f.add_argument("--tap", action="store_true")
    f.set_defaults(fn=cmd_find)

    g = sub.add_parser("gone")
    g.add_argument("label")
    g.add_argument("--at", type=int, nargs=2, required=True, metavar=("X", "Y"))
    g.add_argument("--timeout", type=float, default=10)
    g.set_defaults(fn=cmd_gone)

    r = sub.add_parser("responsive")
    r.add_argument("--timeout", type=float, default=15)
    r.set_defaults(fn=cmd_responsive)

    pr = sub.add_parser("probe")
    pr.add_argument("x", type=int)
    pr.add_argument("y", type=int)
    pr.set_defaults(fn=cmd_probe)

    t = sub.add_parser("tap")
    t.add_argument("x", type=int)
    t.add_argument("y", type=int)
    t.set_defaults(fn=cmd_tap)

    s = sub.add_parser("swipe")
    for n in ("x1", "y1", "x2", "y2"):
        s.add_argument(n, type=int)
    s.set_defaults(fn=cmd_swipe)

    a = p.parse_args()
    UDID = a.udid
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())

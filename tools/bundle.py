"""Bundle the plugin source modules (src/*.cs) into the single deployable file dist/Beastmaster.cs.

Every module is a normal C# file:

    // Reference: Some.Assembly        (optional, hoisted to the top of the bundle)
    using System;
    namespace Oxide.Plugins
    {
        public partial class Beastmaster
        {
            ...
        }
    }

The bundle = generated header + hoisted `// Reference:` lines + the union of all `using` lines +
one `namespace Oxide.Plugins { ... }` holding the body of every module (src/Beastmaster.cs first, the rest
alphabetically). Partial classes in one file compile to one type, which is what Oxide/Carbon load.

Usage:  python -I tools/bundle.py [--src src] [--out dist/Beastmaster.cs]
Exit code 1 on any structural problem (the build stops).
"""
import argparse
import os
import re
import sys

NAMESPACE_RE = re.compile(r"^\s*namespace\s+Oxide\.Plugins\s*$")
USING_RE = re.compile(r"^\s*using\s+[A-Za-z_][\w.]*(\s*=\s*[\w.<>, ]+)?\s*;\s*$")
REFERENCE_RE = re.compile(r"^\s*//\s*Reference:\s*(\S+)\s*$", re.IGNORECASE)


def split_module(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        lines = f.read().replace("\r\n", "\n").split("\n")
    usings, references = [], []
    ns_index = None
    for i, line in enumerate(lines):
        if NAMESPACE_RE.match(line):
            ns_index = i
            break
        m = REFERENCE_RE.match(line)
        if m:
            references.append(m.group(1))
        elif USING_RE.match(line):
            usings.append(line.strip())
        elif line.strip() and not line.strip().startswith("//"):
            raise ValueError("%s:%d: only usings, comments and `// Reference:` may precede the namespace" % (path, i + 1))
    if ns_index is None:
        raise ValueError("%s: missing `namespace Oxide.Plugins`" % path)
    rest = lines[ns_index + 1:]
    open_i = next((i for i, l in enumerate(rest) if l.strip() == "{"), None)
    close_i = max((i for i, l in enumerate(rest) if l.strip() == "}"), default=None)
    if open_i is None or close_i is None or close_i <= open_i:
        raise ValueError("%s: cannot find the namespace braces" % path)
    trailing = [l for l in rest[close_i + 1:] if l.strip()]
    if trailing:
        raise ValueError("%s: text after the namespace closing brace" % path)
    body = rest[open_i + 1:close_i]
    return usings, references, body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="src")
    ap.add_argument("--out", default=os.path.join("dist", "Beastmaster.cs"))
    args = ap.parse_args()

    files = sorted(f for f in os.listdir(args.src) if f.endswith(".cs"))
    if "Beastmaster.cs" not in files:
        print("bundle: src/Beastmaster.cs (the main module with [Info]) is missing", file=sys.stderr)
        return 1
    files.remove("Beastmaster.cs")
    files.insert(0, "Beastmaster.cs")

    all_usings, all_refs, bodies = set(), [], []
    try:
        for name in files:
            usings, refs, body = split_module(os.path.join(args.src, name))
            all_usings.update(usings)
            for r in refs:
                if r not in all_refs:
                    all_refs.append(r)
            bodies.append((name, body))
    except ValueError as ex:
        print("bundle: " + str(ex), file=sys.stderr)
        return 1

    out = []
    out.append("// ------------------------------------------------------------------------------------------")
    out.append("// Beastmaster - generated file. DO NOT EDIT: edit src/*.cs and run tools/build.ps1.")
    out.append("// Modules: " + ", ".join(n for n, _ in bodies))
    out.append("// ------------------------------------------------------------------------------------------")
    for r in all_refs:
        out.append("// Reference: " + r)
    system_first = sorted(all_usings, key=lambda u: (0 if u.startswith("using System") else 1, u))
    out.extend(system_first)
    out.append("")
    out.append("namespace Oxide.Plugins")
    out.append("{")
    for i, (name, body) in enumerate(bodies):
        out.append("    // ======================================== " + name)
        out.extend(body)
        if i < len(bodies) - 1:
            out.append("")
    out.append("}")
    out.append("")

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))
    print("bundle: %s (%d modules, %d lines)" % (args.out, len(bodies), len(out)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

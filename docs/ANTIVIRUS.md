# "My antivirus is flagging Pilaster"

Short answer: **false positive, but your suspicion is fair** — and there's plenty you can do to avoid just taking our word for it.

## Why it happens

Four things come together, and each one is suspicious to a heuristic on its own:

1. **No code signing.** A certificate costs $200–600/year. Without one, Windows SmartScreen and most antivirus engines treat the program as an "unknown publisher."
2. **Zero reputation.** A brand-new binary nobody has run yet is treated as suspicious by default by cloud-based reputation systems. This improves on its own as the download count grows.
3. **.NET, self-contained packaging.** .NET apps are a common target for generic heuristics, since a lot of ransomware is also written in .NET.
4. **We used to ship a single-file package.** The self-extracting exe wrote native libraries to disk on startup — behaviorally identical to how malware downloaders operate.

## What we've already done

**No more single-file package since v0.1.1.** The installer unpacks files into a normal folder and extracts nothing at runtime. This eliminates point 4, and speeds up cold start as a side effect.

**The installer itself follows the same rule.** Pilaster's own custom-built installer (replacing the earlier Inno Setup one) ships as a ZIP you unzip yourself, not a self-extracting `.exe` — the installer executable is a plain, multi-file, self-contained app, exactly like Pilaster itself. No runtime self-extraction anywhere in the download.

What we **can't** do: sign the binary. Until there's a code-signing certificate, false positives will keep coming back from time to time. We're not going to sugarcoat that.

## How to verify it yourself

Don't take our word for it — check:

**1. Checksum.** Every release ships a `.sha256` file. After downloading:

```powershell
Get-FileHash .\Pilaster-0.1.1-x64-setup.zip -Algorithm SHA256
```

If the value matches what's in the release, the file is bit-for-bit what GitHub built.

**2. See where it was built.** The binaries aren't uploaded from a developer's machine: the
[release workflow](../.github/workflows/release.yml) builds them on a GitHub Actions runner, with a public log. The [run log](https://github.com/GREG13-PRO/pilaster/actions) is open for anyone to inspect, and every command in it is visible.

**3. Build it yourself.** The full source is here, with no binary dependencies outside NuGet:

```powershell
git clone https://github.com/GREG13-PRO/pilaster.git
cd pilaster
dotnet publish src/Pilaster.App -c Release -r win-x64 --self-contained
```

**4. VirusTotal.** Upload the file to [virustotal.com](https://www.virustotal.com). You'll typically see 1–3 engines out of 70 flag it, all with generic names (`Win32:Malware-gen`, `ML.Attribute.HighConfidence`, and similar) — these are machine-learning guesses, not identifications of a specific piece of malware.

## If AVG quarantined it

1. **AVG → Menu → Quarantine** — you'll find the file there, and can restore it from there.
2. **Add an exception:** AVG → Menu → Settings → General → Exceptions → Add Exception, and point it at the install folder (`%LOCALAPPDATA%\Programs\Pilaster`).
3. **Report it as a false positive** — this helps everyone else too:
   [avg.com/false-positive-file-form](https://www.avg.com/en-ww/false-positive-file-form)

AVG and Avast use the same engine, so reporting to one is enough.

## When you should NOT assume it's a false positive

Be suspicious if:

- You **didn't download it from the official releases page** (`github.com/GREG13-PRO/pilaster/releases`).
- **The checksum doesn't match.**
- Dozens of engines flag it, not just one or two.
- The alert names a specific malware family, rather than a generic `-gen` or `ML.` label.

In that case the file may genuinely have been tampered with — delete it, and open an [issue](https://github.com/GREG13-PRO/pilaster/issues).

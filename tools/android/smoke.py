"""Smoke test of the Android companion on an emulator (or a phone with USB debugging).

    python tools/android/smoke.py [path/to/fr.ms3dprint.compagnon-Signed.apk]

What it does, from a fresh install: starts the app, picks the exchange folder with the Android file picker, checks that the picture of the
workshop published by the PC is read, notes a purchase, and checks the files that land in the exchange folder.
Needs: adb (ANDROID_HOME or %LOCALAPPDATA%/Android/Sdk), one connected device, and the sample picture next to this file.
The text typed through adb must be ASCII (adb cannot type accents).
"""
import json, os, re, subprocess, sys, time
import xml.etree.ElementTree as ET

SDK = os.environ.get("ANDROID_HOME") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "Android", "Sdk")
ADB = os.path.join(SDK, "platform-tools", "adb.exe" if os.name == "nt" else "adb")
HERE = os.path.dirname(os.path.abspath(__file__))
PACKAGE = "fr.ms3dprint.compagnon"
DEFAULT_APK = os.path.normpath(os.path.join(HERE, "..", "..", "src", "MS3DPRINT.Manager.Android", "bin", "Release", "net10.0-android", PACKAGE + "-Signed.apk"))
FOLDER = "/sdcard/Documents/Echanges mobile"
env = dict(os.environ, MSYS_NO_PATHCONV="1")


def adb(*args, text=True):
    return subprocess.run([ADB, *args], capture_output=True, text=text, encoding="utf-8" if text else None, errors="replace", env=env).stdout


def dump():
    adb("shell", "uiautomator", "dump", "/sdcard/ui.xml")
    xml = adb("exec-out", "cat", "/sdcard/ui.xml")
    return ET.fromstring(xml[xml.find("<?xml"):])


def nodes():
    for n in dump().iter("node"):
        m = re.match(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", n.get("bounds", ""))
        if m:
            x1, y1, x2, y2 = map(int, m.groups())
            yield n, (x1 + x2) // 2, (y1 + y2) // 2


def texts():
    return [(n.get("text") or n.get("content-desc") or "").strip() for n, _, _ in nodes() if n.get("text") or n.get("content-desc")]


def tap(label, exact=False, wait=0.8):
    for n, x, y in nodes():
        value = n.get("text") or ""
        description = n.get("content-desc") or ""
        hit = label in (value, description) if exact else label.lower() in (value + "|" + description).lower()
        if hit:
            adb("shell", "input", "tap", str(x), str(y)); time.sleep(wait); return True
    return False


def tap_xy(x, y, wait=0.8): adb("shell", "input", "tap", str(x), str(y)); time.sleep(wait)
def key(code): adb("shell", "input", "keyevent", str(code))


def scroll(down=True, times=1):
    for _ in range(times):
        a, b = (2000, 600) if down else (600, 2000)
        adb("shell", "input", "swipe", "15", str(a), "15", str(b), "400"); time.sleep(0.7)


def type_into(hint, value):
    if not tap(hint): return False
    time.sleep(0.4)
    adb("shell", "input", "keycombination", "KEYCODE_CTRL_LEFT", "KEYCODE_A"); key(67)
    adb("shell", "input", "text", value.replace(" ", "%s")); key(4); time.sleep(0.5)
    return True


results = []


def check(name, ok, detail=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (" — " + detail if detail and not ok else ""))


def main():
    apk = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_APK
    if PACKAGE not in adb("shell", "pm", "list", "packages", PACKAGE) and not os.path.exists(apk):
        sys.exit("APK introuvable : " + apk)
    if "device" not in adb("devices").split("\n", 1)[1]:
        sys.exit("Aucun appareil connecté (adb devices).")
    adb("uninstall", PACKAGE)
    check("installation", "Success" in adb("install", "-r", apk))
    adb("shell", f"rm -rf '{FOLDER}'"); adb("shell", f"mkdir -p '{FOLDER}'")
    adb("push", os.path.join(HERE, "etat-pc-exemple.json"), FOLDER + "/etat-pc.json")
    adb("shell", "am", "start", "-n", PACKAGE + "/crc64e3f0437053d7f200.MainActivity")
    time.sleep(9)
    check("démarrage", PACKAGE in adb("shell", "pidof", PACKAGE) or bool(adb("shell", "pidof", PACKAGE).strip()))
    check("premier lancement : ouvre les Réglages", "Dossier d’échange non choisi" in " ".join(texts()))

    # pick the exchange folder with the system picker
    tap("Choisir le dossier d"); time.sleep(3)
    if not any(t == "Echanges mobile" for t in texts()):
        # the system picker opens wherever it was last: go to Documents > Echanges mobile
        tap("Show roots", exact=False, wait=1.5) or tap("Afficher les racines", wait=1.5)
        for root in ("sdk_gphone", "Stockage interne", "Internal storage"):
            if tap(root, wait=1.5): break
        tap("Documents", exact=True, wait=1.5)
        tap("Echanges mobile", exact=True, wait=1.5)
    tap("USE THIS FOLDER", exact=True, wait=2) or tap("Utiliser ce dossier", exact=True, wait=2)
    tap("ALLOW", exact=True, wait=4) or tap("AUTORISER", exact=True, wait=4)
    all_text = " ".join(texts())
    check("dossier d’échange choisi", "Dossier choisi" in all_text, all_text[:200])
    check("état du PC lu (6 projets)", "6 projets" in all_text, all_text[:300])
    check("test d’accès au dossier", (tap("Vérifier l’accès") and time.sleep(4) is None and "lecture et écriture possibles" in " ".join(texts())))

    # projects tab
    tap_xy(324, 2260, 1.5)
    check("liste des projets", "Support de caméra réglable" in " ".join(texts()))
    tap_xy(540, 2260, 1.5)
    check("activité : seuils", "Seuils d’activité" in " ".join(texts()))

    # note a purchase
    tap_xy(108, 2260, 1.5)
    type_into("Nom du fournisseur", "Smoke Fournisseur"); type_into("Ex. Visserie", "Test automatique"); type_into("0,00", "12,34")
    scroll(True, 8)
    tap("Envoyer l’achat", wait=5)
    scroll(False, 8)
    check("achat envoyé", any("Envoyé : Smoke Fournisseur" in t for t in texts()), " | ".join(texts())[:300])
    listing = adb("shell", f"ls '{FOLDER}'")
    names = [n for n in listing.split() if n.startswith("achat-") and n.endswith(".json")]
    check("fichier d’échange présent", len(names) == 1, listing)
    if names:
        data = json.loads(adb("shell", f"cat '{FOLDER}/{names[0]}'"))
        check("contenu du fichier", data.get("Supplier") == "Smoke Fournisseur" and abs(data.get("Amount", 0) - 12.34) < 0.001, str(data))

    # back button: from another tab it returns to the first screen
    tap_xy(972, 2260, 1.2); key(4); time.sleep(1.2)
    check("bouton retour", "Saisir" in texts()[:3])
    print(f"\n{sum(results)}/{len(results)} vérifications réussies")
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()

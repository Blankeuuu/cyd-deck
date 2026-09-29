import os

Import("env")


canonical_core = os.environ.get("STREAMDECK_PIO_CORE_CANONICAL_DIR")
if canonical_core:
    esptool = os.path.join(
        canonical_core,
        "packages",
        "tool-esptoolpy",
        "esptool.py",
    )
    if not os.path.isfile(esptool):
        raise RuntimeError(f"Canonical esptool package is missing: {esptool}")
    env.Replace(OBJCOPY=esptool)

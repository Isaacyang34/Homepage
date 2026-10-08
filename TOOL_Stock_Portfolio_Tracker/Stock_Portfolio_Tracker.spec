# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=[('app_icon.ico', '.'), ('app_icon.png', '.'), ('update_icon.ico', '.'), ('update_icon.png', '.'), ('version.json', '.'), ('app_core.pkg', '.'), ('config', 'config'), ('plugins', 'plugins'), ('assets', 'assets'), ('integrity_checker.py', '.')],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['numpy', 'pandas', 'scipy', 'yfinance', 'matplotlib', 'pytest', 'IPython'],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='Stock_Portfolio_Tracker',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon='app_icon.ico',
)

# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - Windowed & Null-Stdio Safety Test Suite
專門針對 Windows GUI / PyInstaller --noconsole 模式下 sys.stdout/sys.stderr 為 None 的致命盲區進行自動化測試防禦：

1. TestASTStaticScan:
   使用 Python AST 遍歷專案所有 .py 檔，檢測是否存在任何對 sys.stdout / sys.stderr 之
   .reconfigure(), .write(), .flush() 裸調用，強制要求必須有 `if sys.stdout is not None` 或 try-except 防護。

2. TestRuntimeNullStdioImport:
   在記憶體中動態將 sys.stdout, sys.stderr, sys.stdin 置為 None，模擬真實 PyInstaller --noconsole 環境，
   強制批次動態載入所有模組，確保 0 個模組在無主控台狀態下載入崩潰。

3. TestSubprocessDevnullLaunch:
   以子程序實體在無主控台與完全隔離之 stdio 管道下執行啟動自我校驗，確保 Exit Code 為 0。
"""
import os
import sys
import ast
import unittest
import importlib
import subprocess

# 專案路徑設定
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODULES_DIR = os.path.join(BASE_DIR, "modules")
if BASE_DIR not in sys.path:
    sys.path.insert(0, BASE_DIR)
if MODULES_DIR not in sys.path:
    sys.path.insert(0, MODULES_DIR)


class StdioAstVisitor(ast.NodeVisitor):
    """AST 靜態語法檢查器：尋找 sys.stdout / sys.stderr / sys.stdin 上的危險方法裸調用"""

    DANGEROUS_METHODS = {"reconfigure", "write", "flush", "writelines"}

    def __init__(self, filepath: str):
        self.filepath = filepath
        self.violations = []
        self._if_guard_stack = []
        self._try_stack = []

    def visit_If(self, node):
        # 檢查 if 條件是否保護了 sys.stdout / sys.stderr
        condition_str = ast.dump(node.test)
        is_guarded = ("stdout" in condition_str or "stderr" in condition_str) and (
            "IsNot" in condition_str or "hasattr" in condition_str or "Not" in condition_str
        )
        self._if_guard_stack.append(is_guarded)
        self.generic_visit(node)
        self._if_guard_stack.pop()

    def visit_Try(self, node):
        self._try_stack.append(True)
        self.generic_visit(node)
        self._try_stack.pop()

    def visit_Call(self, node):
        # 檢查是否為 sys.stdout.<method>(...) 或 sys.stderr.<method>(...)
        if isinstance(node.func, ast.Attribute):
            attr_name = node.func.attr
            if attr_name in self.DANGEROUS_METHODS:
                target = node.func.value
                if isinstance(target, ast.Attribute) and isinstance(target.value, ast.Name):
                    if target.value.id == "sys" and target.attr in ("stdout", "stderr", "stdin"):
                        # 檢查目前是否處於防護區塊內
                        in_guard = any(self._if_guard_stack) or any(self._try_stack)
                        if not in_guard:
                            self.violations.append({
                                "file": self.filepath,
                                "line": node.lineno,
                                "col": node.col_offset,
                                "stream": f"sys.{target.attr}",
                                "method": attr_name,
                                "code": f"sys.{target.attr}.{attr_name}(...)"
                            })
        self.generic_visit(node)


class TestWindowedStdioSafety(unittest.TestCase):
    """無主控台與空串流 (Null-Stdio) 專項防禦測試"""

    def setUp(self):
        self.py_files = []
        for root, dirs, files in os.walk(BASE_DIR):
            # 排除暫存檔與虛擬環境
            if any(ign in root for ign in [".git", "__pycache__", "temp_pkg_stage", "build", "dist"]):
                continue
            for f in files:
                if f.endswith(".py"):
                    self.py_files.append(os.path.join(root, f))

    def test_00_ast_visitor_self_verification(self):
        """測試 0: 驗證 AST 語法檢查器自身的靈敏度 (刻意餵入危險裸調用與安全寫法)"""
        bad_code = (
            "import sys\n"
            "sys.stdout.reconfigure(encoding='utf-8')\n"
            "sys.stderr.write('fatal error')\n"
        )
        bad_tree = ast.parse(bad_code)
        bad_visitor = StdioAstVisitor("test_dummy_bad.py")
        bad_visitor.visit(bad_tree)
        self.assertEqual(len(bad_visitor.violations), 2, "未正確偵測出 2 處未防護的危險裸調用！")
        self.assertEqual(bad_visitor.violations[0]["method"], "reconfigure")
        self.assertEqual(bad_visitor.violations[1]["method"], "write")

        good_code = (
            "import sys\n"
            "if sys.stdout is not None and hasattr(sys.stdout, 'reconfigure'):\n"
            "    sys.stdout.reconfigure(encoding='utf-8')\n"
            "try:\n"
            "    sys.stderr.write('safe error')\n"
            "except Exception:\n"
            "    pass\n"
        )
        good_tree = ast.parse(good_code)
        good_visitor = StdioAstVisitor("test_dummy_good.py")
        good_visitor.visit(good_tree)
        self.assertEqual(len(good_visitor.violations), 0, "合法的防護寫法被誤判為違規！")
        print("[PASS] AST 檢查器自驗證通過: 精準識別裸調用與合格防護。")

    def test_01_ast_static_scan_no_bare_stdio_calls(self):
        """測試 1: AST 靜態掃描專案所有 .py 檔，嚴禁任何 sys.stdout/sys.stderr 裸調用"""
        total_violations = []
        scanned_count = 0

        for filepath in self.py_files:
            # 排除 tools 資料夾中一次性的歷史輔助腳本，但核心模組、main.py 與 tests 必須嚴格全檢
            rel_path = os.path.relpath(filepath, BASE_DIR)
            if rel_path.startswith("tools\\") or rel_path.startswith("tools/"):
                # tools 中若為構建或打包工具仍需檢查
                if not any(k in rel_path for k in ["build", "package", "bump"]):
                    continue

            try:
                with open(filepath, "r", encoding="utf-8") as f:
                    source = f.read()
                tree = ast.parse(source, filename=filepath)
                visitor = StdioAstVisitor(rel_path)
                visitor.visit(tree)
                total_violations.extend(visitor.violations)
                scanned_count += 1
            except Exception as e:
                self.fail(f"無法解析 Python 檔案 AST: {rel_path}, 錯誤: {e}")

        violation_messages = []
        for v in total_violations:
            violation_messages.append(
                f"  -> [{v['file']}:{v['line']}] 致命裸調用: {v['code']} (未受 `if {v['stream']} is not None` 或 try-except 保護)"
            )

        self.assertEqual(
            len(total_violations),
            0,
            msg=f"\n發現 {len(total_violations)} 處未防護的 stdio 裸調用 (在 --noconsole 模式下將導致崩潰):\n"
            + "\n".join(violation_messages)
        )
        print(f"\n[PASS] AST 靜態掃描完成: 共掃描 {scanned_count} 個 Python 模組，0 處裸調用違規。")

    def test_02_runtime_null_stdio_simulation(self):
        """測試 2: 記憶體強制置空 sys.stdout/sys.stderr，動態匯入專案所有核心模組驗證 0 崩潰"""
        original_stdout = sys.stdout
        original_stderr = sys.stderr
        original_stdin = sys.stdin

        critical_modules = [
            "stock_fundamental_service",
            "fundamental_analyzer_gui",
            "unified_ranking_gui",
            "market_ranking_gui",
            "ranking_service",
            "pnl_calculator",
            "dividend_service",
            "margin_risk_service",
            "portfolio_etf_service",
            "database",
            "updater",
            "integrity_checker"
        ]

        loaded_success = []
        failed_modules = []

        try:
            # 模擬 PyInstaller --noconsole 極端環境
            sys.stdout = None
            sys.stderr = None
            sys.stdin = None

            for mod_name in critical_modules:
                try:
                    if mod_name in sys.modules:
                        mod = importlib.reload(sys.modules[mod_name])
                    else:
                        mod = importlib.import_module(mod_name)
                    loaded_success.append(mod_name)
                except AttributeError as ae:
                    failed_modules.append((mod_name, f"AttributeError: {ae}"))
                except Exception as ex:
                    # 非 AttributeError 可能因缺少 GUI 視窗等，但 stdio 相關的 AttributeError 必須為 0
                    if "reconfigure" in str(ex) or "NoneType" in str(ex):
                        failed_modules.append((mod_name, f"StdioCrash: {ex}"))

        finally:
            # 恢復串流以利回報
            sys.stdout = original_stdout
            sys.stderr = original_stderr
            sys.stdin = original_stdin

        self.assertEqual(
            len(failed_modules),
            0,
            msg=f"在 sys.stdout=None 環境下載入模組發生崩潰: {failed_modules}"
        )
        print(f"[PASS] 模擬無主控台載入測試完成: 成功載入 {len(loaded_success)} 個核心模組，無任何 NoneType 錯誤。")

    def test_03_subprocess_isolated_null_stdio_startup(self):
        """測試 3: 獨立子程序以 DEVNULL 串流管道啟動驗證"""
        env = os.environ.copy()
        pythonpath_parts = [MODULES_DIR, BASE_DIR]
        if "PYTHONPATH" in env:
            pythonpath_parts.append(env["PYTHONPATH"])
        env["PYTHONPATH"] = os.pathsep.join(pythonpath_parts)

        test_script = (
            "import sys\n"
            "sys.stdout = None\n"
            "sys.stderr = None\n"
            "import stock_fundamental_service\n"
            "import fundamental_analyzer_gui\n"
            "import unified_ranking_gui\n"
            "sys.exit(0)\n"
        )
        proc = subprocess.run(
            [sys.executable, "-c", test_script],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=env,
            timeout=10
        )
        self.assertEqual(proc.returncode, 0, msg=f"子程序在 Null-Stdio 條件下載入異常，退出碼: {proc.returncode}")
        print("[PASS] 子程序無主控台隔離測試完成: Exit Code = 0。")


if __name__ == "__main__":
    unittest.main()

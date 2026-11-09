"""Browser-language and explicit-override smoke tests for EverPT Web.

Usage: python tests/locale_smoke.py <module-revision> [base-url]
The base URL must serve locale-qa.html and the versioned JS from the same origin.
No game account or server data is used in these tests.
"""
import json
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright

revision = sys.argv[1]
base = sys.argv[2] if len(sys.argv) > 2 else "http://127.0.0.1:18920"
cases = [
    ("br-first", "pt-BR", None, None, "pt-BR"),
    ("english-first", "en-US", None, None, "en"),
    ("br-legacy-english", "pt-BR", {"version": 1, "language": "en"}, None, "pt-BR"),
    ("br-explicit-english", "pt-BR", {"version": 1, "language": "en"}, "en", "en"),
    ("en-explicit-ptbr", "en-US", None, "pt-BR", "pt-BR"),
    ("pt-PT", "pt-PT", None, None, "pt-BR"),
]
result = {"cases": []}
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel="chrome", headless=True)
    for name, locale, saved, choice, expected in cases:
        context = browser.new_context(locale=locale)
        page = context.new_page()
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)[:200]))
        page.goto(base + "/locale-qa.html", wait_until="domcontentloaded", timeout=25000)
        if saved:
            page.evaluate("(x) => localStorage.setItem('etervale-client-settings',JSON.stringify(x))", saved)
        if choice:
            page.evaluate("(x) => localStorage.setItem('everpt-language-choice',x)", choice)
        data = page.evaluate("""async revision => {
            const i18n = await import('/i18n.'+revision+'.js');
            const settings = await import('/settings.'+revision+'.js');
            const fs = { value: '', writeFile(path,text) { this.value = text; },
                readFile() { return this.value; } };
            settings.restoreSettings(fs);
            return { language:i18n.currentLanguage(), stored:JSON.parse(fs.value).language,
                html:document.documentElement.lang, note:document.querySelector('#note')?.textContent };
        }""", revision)
        passed = data["language"] == expected and data["stored"] == expected and data["html"] == expected and not errors
        result["cases"].append({"case": name, "pass": passed, "language": data["language"], "errors": errors})
        if name == "br-first":
            panel = page.evaluate("""async revision => {
                const settings = await import('/settings.'+revision+'.js');
                settings.startSettings({ ApplySettings:()=>Promise.resolve() });
                document.querySelector('#tools button').click();
                const before = { title:document.querySelector('#panel h2')?.textContent,
                    label:document.querySelector('label[for=mg-language]')?.textContent,
                    value:document.querySelector('#mg-language')?.value };
                const field=document.querySelector('#mg-language');
                field.value='en'; field.dispatchEvent(new Event('change',{bubbles:true}));
                return { before, after:{ language:document.documentElement.lang,
                    choice:localStorage.getItem('everpt-language-choice'),
                    title:document.querySelector('#panel h2')?.textContent } };
            }""", revision)
            result["manual"] = {"pass":
                panel["before"]["title"] == "Configurações" and
                panel["before"]["label"] == "Idioma" and
                panel["after"]["language"] == "en" and
                panel["after"]["choice"] == "en" and
                panel["after"]["title"] == "Settings", **panel}
        context.close()
    browser.close()
print(json.dumps(result, ensure_ascii=False, indent=2))
if not all(x["pass"] for x in result["cases"]) or not result["manual"]["pass"]:
    sys.exit(1)

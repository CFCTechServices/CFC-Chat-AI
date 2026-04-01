# CI/CD Deployment Test Plan

This document defines all checks that should run in an automated CI/CD pipeline before any code is deployed to the production VM. No pipeline exists yet — this is the reference for when it is implemented.

---

## Checks to Run on Every Pull Request (GitHub Actions)

### 1. Python Syntax & Import Check
Catch broken imports and syntax errors before they reach the server.
```bash
python -m py_compile main.py
python -c "import main"
```

### 2. Unit Tests
Run the existing test suite.
```bash
pytest tests/ -v --tb=short
```

### 3. Required Environment Variables Check
Verify all required `.env` keys are present in `.env.example` (no undocumented secrets).
```bash
# Compare keys in .env.example vs what the app requires at runtime
python -c "
from app.config import Settings
import os
s = Settings()
print('Config loads OK')
"
```

### 4. Dependency Security Scan
Catch known vulnerabilities in Python packages.
```bash
pip install pip-audit
pip-audit -r requirements.txt
```

### 5. Linting
Catch obvious code issues.
```bash
pip install ruff
ruff check app/ main.py
```

---

## Checks to Run Before Deploying to Production (Pre-Deploy Gate)

### 6. API Health Check (Post-Deploy Smoke Test)
After deployment, confirm the backend is responding.
```powershell
# Run on VM after deploy
$response = Invoke-RestMethod http://127.0.0.1:8000/api/health
if ($response.ok -ne $true) { throw "Health check failed" }
Write-Host "Health check passed: $($response.message)"
```

### 7. Windows Service Running Check
Confirm NSSM service is in the correct state.
```powershell
$status = nssm status CFC-ChatAI
if ($status -ne "SERVICE_RUNNING") { throw "Service not running: $status" }
```

### 8. Frontend Reachability Check
Confirm IIS is serving the frontend.
```powershell
$code = (Invoke-WebRequest http://127.0.0.1/ui/ -UseBasicParsing).StatusCode
if ($code -ne 200) { throw "Frontend returned $code" }
```

### 9. API Route Prefix Check
Guard against the recurring merge regression where `/api` prefixes are lost.
```bash
python -c "
import main, inspect
routes = [r.path for r in main.app.routes]
for route in routes:
    if route not in ['/', '/docs', '/openapi.json', '/redoc']:
        assert route.startswith('/api'), f'Route missing /api prefix: {route}'
print('All routes have correct /api prefix')
"
```

### 10. CORS Not Wildcard Check
Guard against another recurring regression — CORS reverting to `*`.
```bash
python -c "
import os
origins = os.environ.get('CORS_ORIGINS', '')
assert origins != '*', 'CORS_ORIGINS is set to wildcard — check .env'
assert 'localhost' not in origins or os.environ.get('ENV') == 'development', \
    'CORS_ORIGINS contains localhost in production'
print(f'CORS origins OK: {origins}')
"
```

---

## GitHub Actions Setup (When Ready to Implement)

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main, deployment-test]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Set up Python 3.11
        uses: actions/setup-python@v5
        with:
          python-version: "3.11"

      - name: Install CPU-only torch
        run: pip install torch --index-url https://download.pytorch.org/whl/cpu

      - name: Install dependencies
        run: pip install -r requirements.txt

      - name: Syntax check
        run: python -m py_compile main.py

      - name: Route prefix guard
        run: |
          python -c "
          import main
          routes = [r.path for r in main.app.routes]
          skip = {'/', '/docs', '/openapi.json', '/redoc'}
          for r in routes:
              if r not in skip:
                  assert r.startswith('/api'), f'Missing /api prefix: {r}'
          print('OK')
          "

      - name: Lint
        run: |
          pip install ruff
          ruff check app/ main.py

      - name: Security audit
        run: |
          pip install pip-audit
          pip-audit -r requirements.txt

      - name: Run tests
        run: pytest tests/ -v --tb=short
        env:
          PINECONE_API_KEY: ${{ secrets.PINECONE_API_KEY }}
          SUPABASE_URL: ${{ secrets.SUPABASE_URL }}
          SUPABASE_ANON_KEY: ${{ secrets.SUPABASE_ANON_KEY }}
          SUPABASE_SERVICE_ROLE_KEY: ${{ secrets.SUPABASE_SERVICE_ROLE_KEY }}
          AZURE_OPENAI_API_KEY: ${{ secrets.AZURE_OPENAI_API_KEY }}
          AZURE_OPENAI_ENDPOINT: ${{ secrets.AZURE_OPENAI_ENDPOINT }}
          AZURE_OPENAI_DEPLOYMENT: ${{ secrets.AZURE_OPENAI_DEPLOYMENT }}
          AZURE_OPENAI_API_VERSION: ${{ secrets.AZURE_OPENAI_API_VERSION }}
          CORS_ORIGINS: http://localhost:8000
```

### How to Add Secrets to GitHub
1. Go to your GitHub repo → **Settings** → **Secrets and variables** → **Actions**
2. Add each key from `.env.example` as a **Repository secret**
3. Never commit actual `.env` values — the workflow reads them from secrets

### Branch Protection Rules (Recommended)
In GitHub → **Settings** → **Branches** → protect `main`:
- ✅ Require status checks to pass before merging
- ✅ Require the `test` job to pass
- ✅ Require pull request review before merging
- ✅ Restrict who can push directly to `main`

---

## Priority Order for Implementation

| Priority | Check | Why |
|----------|-------|-----|
| High | Route prefix guard (#9) | Has broken production twice |
| High | Health check (#6) | Catches most deploy failures |
| High | Unit tests (#2) | Core correctness |
| Medium | CORS check (#10) | Has broken production |
| Medium | Service running check (#7) | Deployment verification |
| Low | Security audit (#4) | Good hygiene |
| Low | Linting (#5) | Code quality |

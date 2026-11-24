# Goal
Full repository reorganization

# Requirements
1. OPERATIONS_CENTRAL.md, ADMIN_SETUP_GUIDE.md, DEPLOYMENT.md, DEPLOYMENT_SETUP.md, HANDOVER_DOCUMENT.md, RUNBOOK.md, SETUP_GUIDE.md, TEAM_ACCESS_GUIDE.md. These should all be merged into one, up to date, README.md. Others including CICD_PLAN.md, EMAIL_SETUP.md, and DOTNET-IIS.md can persist but should made sure up to date.
2. Old Files should be hidden. Create a folder that will contain all unused files. MIGRATION_GUIDE_CSHARP.md, ./scripts/ folder, ./tests/ folder, ./CFC.ChatAI.slnx, main.py, pytest.ini, requirements.txt, ./web/ folder, ./backup/ folder, ./app/ folder. At least these current files/folders should be hidden away.
3. New files should be moved up. Specifically the dotnet/ folder. It should exist at the repo root and not in a dotnet subdirectory. Merge the dotnet/docs/ folder with the existing docs/.
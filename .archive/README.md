# Legacy repository archive

The active application is the [.NET API and Blazor site](../README.md). This folder preserves the Python/FastAPI application (`main.py`, `app/`), React UI (`web/`), legacy tests and tools, deployment artifacts, sample documents, and the superseded partial .NET solution. Nothing here is needed to build or deploy the active app. The optional .NET document-upload parity harness imports the Python baseline from here; see the [parity report](../docs/tests/document-upload-parity-report.md#reproduction-and-verification).

The old setup, access, operations, deployment, handover, and migration guides under `docs/` are historical and may describe obsolete infrastructure, paths, and procedures. Do not use the archived deployment scripts to update a running site. For rollback, restore the previously deployed, matching site and backend using the [.NET/IIS rollback procedure](../deployment/DOTNET-IIS.md#updates-and-rollback).

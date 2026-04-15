# 🛠️ Development & Setup Guide

This guide provides comprehensive instructions for local development, continuous integration (CI), and a brief overview of the production deployment process.

---

## 📋 Table of Contents

1. [Local Development Setup](#1-local-development-setup)
2. [Running the Application](#2-running-the-application)
3. [Testing](#3-testing)
4. [Continuous Integration (CI)](#4-continuous-integration-ci)
5. [Continuous Deployment (CD)](#5-continuous-deployment-cd)
6. [VM Deployment Overview](#6-vm-deployment-overview)

---

## 1. Local Development Setup

### Prerequisites

- **Python 3.10+**: The application is tested with Python 3.10 and 3.11.
- **Office/LibreOffice**: (Optional) Required for processing `.doc` (Word 97-2003) files on Windows.
- **FFmpeg**: Required for processing video uploads and transcriptions.

### Step-by-Step Installation

1.  **Clone the Repository**:
    ```bash
    git clone <repository-url>
    cd CFC-Chat-AI
    ```

2.  **Create a Virtual Environment**:
    ```bash
    python -m venv .venv
    # Activate macOS/Linux:
    source .venv/bin/activate
    # Activate Windows:
    .venv\Scripts\activate
    ```

3.  **Install Dependencies**:
    ```bash
    pip install -r requirements.txt
    ```

4.  **Environment Variables**:
    Copy the example environment file and fill in the required keys.
    ```bash
    cp .env.example .env
    ```
    **Required Keys**:
    - `PINECONE_API_KEY`: For vector storage and retrieval.
    - `AZURE_OPENAI_API_KEY` or `GEMINI_API_KEY`: For AI answer generation.
    - `SUPABASE_URL` & `SUPABASE_KEY`: For user authentication and storage.

---

## 2. Running the Application

### Start the FastAPI Server

The backend and frontend (static files) are both served by FastAPI.

```bash
uvicorn main:app --reload
```

- **Web UI**: [http://localhost:8000](http://localhost:8000)
- **Swagger Docs**: [http://localhost:8000/docs](http://localhost:8000/docs)
- **Health Check**: [http://localhost:8000/api/health](http://localhost:8000/api/health)

### File Storage

- Uploaded files are stored in `data/documents/`.
- Processed chunks and metadata are stored in `data/processed/`.
- If Supabase is enabled in `.env`, files are also backed up to Supabase storage.

---

## 3. Testing

We use `pytest` for unit and integration testing.

### Running All Tests

```bash
pytest tests/
```

### Running Specific Test Categories

```bash
# API endpoint tests
pytest tests/test_api_endpoints/

# Document processing tests
pytest tests/test_document_processor.py
```

---

## 4. Continuous Integration (CI)

Our CI pipeline is managed via **GitHub Actions**.

### CI Workflow (`.github/workflows/ci.yml`)

- **Trigger**: Every push or Pull Request to the repository (except for certain admin tests).
- **Environment**: Runs on `ubuntu-latest` with Python 3.10.
- **Steps**:
    1.  Checks out code.
    2.  Sets up Python with pip caching.
    3.  Installs all dependencies from `requirements.txt`.
    4.  Runs the test suite using `pytest`.

Ensure all tests pass locally before pushing code to the repository.

---

## 5. Continuous Deployment (CD)

The project uses a **Pull-Based Deployment Strategy** for the production environment.

### Deployment Plan (`docs/CICD_PLAN.md`)

- The production VM (Azure Windows VM) runs a background task that "polls" GitHub for new, approved updates on the `main` branch.
- When an update is detected, the VM automatically:
    1.  Pulls the latest code.
    2.  Runs `deploy-windows.ps1`.
    3.  Restarts the application service.

This ensures that the live application is always synchronized with the tested and approved code in the central repository without requiring manual login to the server.

---

## 6. VM Deployment Overview

For full production deployment details, refer to [**DEPLOYMENT.md**](./DEPLOYMENT.md).

### Summary of VM Architecture

- **OS**: Windows 10/Server (Azure VM).
- **Process Manager**: **NSSM** (Non-Sucking Service Manager) runs the FastAPI app as a Windows Service named `CFC-ChatAI`.
- **Reverse Proxy**: **IIS** (Internet Information Services) handles incoming traffic on ports 80/443 and proxies `/api` requests to the backend service.
- **SSL**: Managed via Win-ACME for Let's Encrypt certificates.

### Deployment Script

To manually trigger a deployment or update on the VM:
```powershell
.\deploy-windows.ps1
```
This script handles the entire lifecycle: environment setup, dependency updates, frontend asset deployment, and service restart.

---

## 🆘 Troubleshooting & Support

- **Credentials**: Contact **Dan Bates** for Pinecone, Supabase, and OpenAI/Gemini access.
- **Common Issues**:
    - **Missing `ffmpeg`**: Transcription will fail.
    - **Pinecone Timeout**: Check if the index is active and the API key is correct.
    - **CORS Errors**: Ensure `CORS_ORIGINS` in `.env` includes your development domain.

---

# Continuous Integration & Continuous Deployment (CI/CD) Plan

## 1. Introduction

**What is CI/CD?**
CI/CD stands for **Continuous Integration** and **Continuous Deployment**. It is a modern software engineering practice that allows teams to deliver code changes more frequently, safely, and reliably. 
- **Continuous Integration (CI):** The practice of automating the testing of new code changes. Every time a developer makes a change, the system automatically checks if the new code breaks anything.
- **Continuous Deployment (CD):** The practice of automatically releasing those tested changes to the live, production environment so that end-users can access the new features immediately.

Together, CI/CD ensures that the software is always in a working state and that new features or bug fixes reach users as quickly as possible without requiring manual, error-prone deployment steps.

## 2. Project Context & Infrastructure Setup

**What project/code is being updated?**
This CI/CD pipeline manages updates for the **CFC Chat-AI Application**. This includes:
- The backend logic (handling the AI chatbot, database interactions, and secure APIs).
- The frontend user interface (what users see and interact with in their web browser).

**Where is this code residing?**
- **Source Code Repository:** The original, central copy of the code lives securely on **GitHub**. This is where developers collaborate, review, and approve all new changes.
- **Production Environment:** The live, running version of the application resides on a **Microsoft Azure Virtual Machine (Windows VM)** hosted in your cloud infrastructure.

The goal of this CI/CD pipeline is to safely and automatically move approved code from the **GitHub repository** directly to the **Azure VM** where the live application runs.

---

## 3. Current State: Continuous Integration (CI)

Our current CI implementation ensures that all code added to the main GitHub repository meets strict quality and reliability standards before it can even be considered for deployment.

*   **Automated Testing:** Whenever a developer proposes a change (via a "Pull Request"), an automated system runs our test suite. This verifies that the new code functions correctly and doesn't introduce bugs.
*   **Quality Controls:** Only code that passes all tests and receives approval from another developer can be merged into the "main" version of the code.
*   **Outcome:** We have high confidence that the code in our central repository is always stable. However, currently, the process of deploying this stable code to the live server on Azure remains a manual task.

---

## 4. Proposed State: Continuous Deployment (CD)

Currently, an administrator must manually log into the Windows VM, download the latest code, and restart the application. To modernize this, we propose an automated **Pull-Based Deployment Strategy**.

### 4.1 Architecture Model: "Pull-Based Polling"

Instead of an external service trying to push code into the secure Azure VM (which could create security vulnerabilities by requiring us to open inbound pathways), the VM will proactively "poll" (or check) GitHub for approved updates. 

#### **Mechanism:**
1.  **Scheduled Polling:** A small, automated background task will run directly on the VM on a regular schedule (e.g., every 5 minutes).
2.  **Version Checking:** The script looks at GitHub to see if there are any new, approved updates compared to what is currently running on the server.
3.  **Automated Execution:** 
    *   If no changes are detected, the system does nothing.
    *   If a new version is found, the system begins the update process automatically.

### 4.2 The Deployment Sequence

Upon detecting a new, approved update on GitHub, the automated script on the VM executes the following steps:

1.  **Secure Download:** The server securely pulls the latest, approved code from GitHub.
2.  **Execute Deployment Script:** The system runs our standard installation process automatically. It updates the frontend screens, installs any necessary background dependencies, and restarts the backend services to apply the changes.
3.  **Notification:** The pipeline sends an automated message to the development team (e.g., via Discord or email) confirming that the live application has been successfully updated.

---

## 5. Advantages of this Strategy

*   **Enhanced Security:** By having the VM "pull" updates instead of accepting inbound "pushes", we don't need to open any extra firewall ports to the internet. The server remains highly secure behind your existing network defenses.
*   **Faster Rollouts:** Developers can fix bugs or launch features, and those changes will appear in production within minutes without human intervention.
*   **Reduced Human Error:** Automating the deployment removes the risk of a person missing a step during manual server updates.
*   **Auditability & Trackability:** Every update on the live server can be traced directly back to a specific set of changes approved on GitHub.



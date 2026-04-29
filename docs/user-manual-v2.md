# EasySave v2.0 — User Manual

**EasySave** is a backup tool developed by ProSoft. This manual covers the graphical interface introduced in version 2.0.

---

## 1. Installation

1. Copy the `EasySave.UI` folder to the desired location on your workstation.
2. Run `EasySave.UI.exe` (Windows) or `EasySave.UI` (macOS / Linux).
3. On first launch, default settings are applied. User data is stored under:
   - **Windows**: `%AppData%\ProSoft\EasySave\`
   - **macOS / Linux**: `~/.config/ProSoft/EasySave/`

> [SCREENSHOT : Application launch — main window with sidebar]

---

## 2. Interface overview

The window is divided into two zones:

| Zone | Description |
|------|-------------|
| **Sidebar (left)** | Navigation buttons (Backups, Restore, Schedule, Settings, Logs) and language toggle (FR / EN) |
| **Content area (right)** | Active view |

> [SCREENSHOT : Annotated main window]

---

## 3. Creating a backup job

1. Click **Backups** in the sidebar.
2. Click the **Add** button (top-right of the jobs list).
3. Fill in the form:
   - **Job Name** — unique identifier (e.g. `Documents`).
   - **Source Path** — folder to back up. Use the **Browse** button or type a path directly (local, network share, or external drive).
   - **Destination Path** — where copies are stored.
   - **Backup Type** — `Full` (all files every time) or `Differential` (only files changed since the last full run).
4. Click **Save**.

> [SCREENSHOT : Job edit form]

---

## 4. Running a backup

- **Run a single job**: open the Backups view, locate the job card, click **Run**.
- **Run all jobs**: click **Run All** (top-right). Jobs execute in parallel.
- A progress bar and file counter appear on each card while the job is running.
- Click **Progress** (automatic navigation) to see a consolidated progress screen.

> [SCREENSHOT : Jobs view with one job running]

### 4.1 Pausing and resuming manually

- Click **Pause** on a running job card to pause it at the next file boundary (the file in progress is not interrupted).
- Click **Resume** to continue from where it stopped.

### 4.2 Automatic pause — business software detection

If a *business software* (configured in Settings) is detected while a job runs, all running jobs pause automatically. A warning banner is displayed. Jobs resume automatically when the business software is closed.

> [SCREENSHOT : Business software pause banner]

---

## 5. Configuration (Settings)

Click **Settings** in the sidebar.

| Setting | Description |
|---------|-------------|
| **Encrypted Extensions** | File extensions whose content is encrypted via CryptoSoft before copying (e.g. `.docx`, `.pdf`). |
| **Business Software** | Process names (e.g. `calc.exe`) that trigger an automatic backup pause. |
| **Log Format** | `json` — daily `.json` file; `xml` — daily `.xml` file (XSD-validated). Takes effect on the next application start. |
| **CryptoSoft Path** | Full path to the CryptoSoft executable. Leave empty to disable encryption. |

Click **Save** to persist your changes.

> [SCREENSHOT : Settings view]

---

## 6. Automatic encryption

When a file's extension is in the *Encrypted Extensions* list:

1. EasySave calls CryptoSoft instead of performing a plain copy.
2. The encrypted file is written to the destination.
3. The log entry includes an `EncryptionTimeMs` field with the encryption duration.
4. A negative `EncryptionTimeMs` value indicates an encryption failure (the file is not copied).

Encryption requires CryptoSoft to be installed and its path configured in Settings.

---

## 7. Changing the language (FR / EN)

Click **FR** or **EN** at the bottom of the sidebar. The interface updates immediately without restarting.

> [SCREENSHOT : Language toggle buttons]

---

## 8. Restoring a backup

1. Click **Restore** in the sidebar.
2. Select a backup job from the drop-down list.
3. The list of available restore points is displayed (timestamp, type, size in MB).
4. Select a restore point.
5. Optionally click **Browse** to choose an alternative destination folder. Leave the field empty to restore files back to their original source path.
6. Click **Restore**. A progress bar tracks the operation.

> [SCREENSHOT : Restore view with restore point selected]

---

## 9. Scheduling automatic backups

1. Click **Schedule** in the sidebar.
2. For each job, toggle the **Enabled** checkbox.
3. Set the **Interval** (in minutes) between automatic runs.
4. The **Next run** column shows the next scheduled execution time.
5. Click **Save** to persist the schedule.

> [SCREENSHOT : Schedule view]

---

## 10. Log files

Daily log files are written to the configured `LogDirectory` (default: inside the user data folder).

| Format | File name | Content |
|--------|-----------|---------|
| JSON | `yyyy-MM-dd.json` | JSON array of log entries |
| XML | `yyyy-MM-dd.xml` | `<Logs>` document with `<Entry>` elements |

Each entry contains: timestamp, job name, source path (UNC), target path (UNC), file size, transfer time (ms), and encryption time (ms, omitted when not encrypted). A negative transfer time indicates a copy failure.

---

*EasySave v2.0 — CESI PGE A3 FISE INFO — Software Engineering project*

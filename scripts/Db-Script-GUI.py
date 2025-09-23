import customtkinter as ctk
import subprocess
import platform
import os
import shutil
import threading
import time

# --- PATH CONFIGURATION ---
# IMPORTANT: Adjust PROJECT_ROOT if your script's location relative to the
#            project root is different.
# This assumes the Python script is ONE levels down from the project root.

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))

INFRASTRUCTURE_PROJECT_DIR = os.path.join(
    PROJECT_ROOT, "src", "ArgonFetch.Infrastructure"
)
MIGRATIONS_DIR_IN_INFRA = "Migrations"  # The actual "Migrations" folder name
MIGRATIONS_FULL_PATH = os.path.join(
    INFRASTRUCTURE_PROJECT_DIR, MIGRATIONS_DIR_IN_INFRA
)
DOCKER_COMPOSE_FILE_NAME = "compose.dev.yml"
DOCKER_COMPOSE_FULL_PATH = os.path.join(PROJECT_ROOT, DOCKER_COMPOSE_FILE_NAME)
APP_ICON_NAME = "logo-simple.ico"
APP_ICON_PATH = os.path.join(PROJECT_ROOT, "assets", APP_ICON_NAME)

# For hiding console window on Windows when running subprocesses
if platform.system() == "Windows":
    from subprocess import CREATE_NO_WINDOW


class MigrationManagerGUI(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("Migration & DB Manager (ArgonFetch)")
        self.geometry("800x750")  # Adjusted height slightly for new button

        if os.path.exists(APP_ICON_PATH):
            try:
                self.iconbitmap(APP_ICON_PATH)
            except Exception as e:
                print(f"Error setting icon: {e}")
        else:
            print(f"Warning: Icon file not found at {APP_ICON_PATH}")

        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")

        self.primary_color = "#2d8cff"
        self.danger_color = "#dc3545"
        self.success_color = "#28a745"
        self.warning_color = "#fd7e14"
        self.info_color = "#17a2b8"
        self.purple_color = "#9c27b0"  # For full reset
        self.revert_color = "#6f42c1" # A slightly different purple for revert

        self.status_var = ctk.StringVar(value="Ready")

        self.create_sidebar()
        self.create_main_content()

        self.write_to_console(
            "--- Initial Paths ---\n"
            f"Script Directory: {SCRIPT_DIR}\n"
            f"Project Root: {PROJECT_ROOT}\n"
            f"Infrastructure Project Dir: {INFRASTRUCTURE_PROJECT_DIR}\n"
            f"Migrations Folder Full Path: {MIGRATIONS_FULL_PATH}\n"
            f"Docker Compose File: {DOCKER_COMPOSE_FULL_PATH}\n"
            f"Application Icon: {APP_ICON_PATH}\n"
            "---------------------\n"
        )
        self.check_paths()

    def check_paths(self):
        if not os.path.isdir(PROJECT_ROOT):
            self.write_to_console(
                f"WARNING: PROJECT_ROOT '{PROJECT_ROOT}' does not seem to be a valid directory. Please check path configuration.\n"
            )
        if not os.path.isdir(INFRASTRUCTURE_PROJECT_DIR):
            self.write_to_console(
                f"WARNING: INFRASTRUCTURE_PROJECT_DIR '{INFRASTRUCTURE_PROJECT_DIR}' does not exist.\n"
            )
        if not os.path.isfile(DOCKER_COMPOSE_FULL_PATH):
            self.write_to_console(
                f"WARNING: Docker Compose file '{DOCKER_COMPOSE_FULL_PATH}' not found.\n"
            )
        if not os.path.isfile(APP_ICON_PATH):
            self.write_to_console(
                f"WARNING: Application icon '{APP_ICON_PATH}' not found.\n"
            )

    def create_sidebar(self):
        self.sidebar_frame = ctk.CTkFrame(self, width=200, corner_radius=0)
        self.sidebar_frame.pack(side="left", fill="y")

        self.logo_label = ctk.CTkLabel(
            self.sidebar_frame,
            text="ArgonFetch\nManager",
            font=ctk.CTkFont(size=20, weight="bold"),
        )
        self.logo_label.pack(pady=20)

        self.controls_button = ctk.CTkButton(
            self.sidebar_frame,
            text="Operations",
            fg_color=self.primary_color,
            hover_color="#1a6ed8",
        )
        self.controls_button.pack(pady=10, padx=20)

        self.version_label = ctk.CTkLabel(self.sidebar_frame, text="v1.0.1") # Updated version
        self.version_label.pack(side="bottom", pady=10)

    def create_main_content(self):
        self.main_frame = ctk.CTkFrame(self)
        self.main_frame.pack(side="right", fill="both", expand=True)

        self.main_page_content_frame = ctk.CTkFrame(
            self.main_frame, fg_color="transparent"
        )
        self.main_page_content_frame.pack(
            side="top", fill="both", expand=True
        )

        self.setup_main_page()

        self.status_bar = ctk.CTkLabel(
            self.main_frame,
            textvariable=self.status_var,
            height=25,
            anchor="w",
            font=ctk.CTkFont(size=12),
        )
        self.status_bar.pack(fill="x", side="bottom", padx=10, pady=(5, 5))

    def setup_main_page(self):
        self.main_page = ctk.CTkFrame(
            self.main_page_content_frame, fg_color="transparent"
        )
        self.main_page.pack(fill="both", expand=True)

        self.title_label = ctk.CTkLabel(
            self.main_page,
            text="Development Operations",
            font=ctk.CTkFont(size=24, weight="bold"),
        )
        self.title_label.pack(pady=(20, 10), padx=20, anchor="w")

        self.cards_frame = ctk.CTkScrollableFrame(self.main_page)
        self.cards_frame.pack(fill="both", expand=True, padx=20, pady=10)

        # --- Database Control Card ---
        db_card = ctk.CTkFrame(self.cards_frame, corner_radius=10)
        db_card.pack(fill="x", pady=10, padx=5)
        ctk.CTkLabel(
            db_card,
            text="Database Controls",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(15, 5), padx=15, anchor="w")
        db_buttons_frame = ctk.CTkFrame(db_card, fg_color="transparent")
        db_buttons_frame.pack(fill="x", pady=(0, 15), padx=15)
        ctk.CTkButton(
            db_buttons_frame,
            text="Start DB",
            command=lambda: self.dispatch_command("start_db"),
            fg_color=self.success_color,
            hover_color="#218838",
        ).pack(side="left", padx=5)
        ctk.CTkButton(
            db_buttons_frame,
            text="Stop DB",
            command=lambda: self.dispatch_command("stop_db"),
            fg_color=self.danger_color,
            hover_color="#c82333",
        ).pack(side="left", padx=5)
        ctk.CTkButton(
            db_buttons_frame,
            text="Recreate DB",
            command=lambda: self.dispatch_command("recreate_db"),
            fg_color=self.warning_color,
            hover_color="#e76b00",
        ).pack(side="left", padx=5)

        # --- Migration Card ---
        mig_card = ctk.CTkFrame(self.cards_frame, corner_radius=10)
        mig_card.pack(fill="x", pady=10, padx=5)
        ctk.CTkLabel(
            mig_card,
            text="Migration Controls",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(15, 5), padx=15, anchor="w")
        mig_buttons_frame = ctk.CTkFrame(mig_card, fg_color="transparent")
        mig_buttons_frame.pack(fill="x", pady=(0, 15), padx=15)
        ctk.CTkButton(
            mig_buttons_frame,
            text="Add Migration",
            command=self.ui_action_add_migration,
            fg_color=self.primary_color,
            hover_color="#1a6ed8",
        ).pack(side="left", padx=5)
        ctk.CTkButton( # New Revert Button
            mig_buttons_frame,
            text="Revert Migration",
            command=self.ui_action_revert_migration,
            fg_color=self.revert_color,
            hover_color="#5a32a3",
        ).pack(side="left", padx=5)
        ctk.CTkButton(
            mig_buttons_frame,
            text="Delete Migrations Folder",
            command=self.ui_action_delete_migrations,
            fg_color=self.danger_color,
            hover_color="#c82333",
        ).pack(side="left", padx=5)


        # --- Full Reset Card ---
        reset_card = ctk.CTkFrame(self.cards_frame, corner_radius=10)
        reset_card.pack(fill="x", pady=10, padx=5)
        ctk.CTkLabel(
            reset_card,
            text="Full Project Reset",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(15, 5), padx=15, anchor="w")
        ctk.CTkLabel(
            reset_card,
            text="Deletes migrations, recreates DB, adds 'Init' migration.",
            font=ctk.CTkFont(size=12),
            wraplength=700,
        ).pack(pady=(0, 10), padx=15, anchor="w")
        reset_buttons_frame = ctk.CTkFrame(reset_card, fg_color="transparent")
        reset_buttons_frame.pack(fill="x", pady=(0, 15), padx=15)
        ctk.CTkButton(
            reset_buttons_frame,
            text="Perform Full Reset",
            command=self.ui_action_full_reset,
            fg_color=self.purple_color,
            hover_color="#7b1fa2",
            height=40,
        ).pack(side="left", padx=5)

        # --- Console Output Card ---
        console_card = ctk.CTkFrame(self.cards_frame, corner_radius=10)
        console_card.pack(fill="both", expand=True, pady=10, padx=5)
        ctk.CTkLabel(
            console_card,
            text="Console Output",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(15, 10), padx=15, anchor="w")
        self.create_console(console_card)

    def create_console(self, parent_frame):
        self.console_output = ctk.CTkTextbox(
            parent_frame,
            font=ctk.CTkFont(
                family="Consolas"
                if platform.system() == "Windows"
                else "Courier",
                size=12,
            ),
            wrap="word",
            padx=10,
            pady=10,
        )
        self.console_output.pack(
            fill="both", expand=True, padx=15, pady=(0, 15)
        )
        self.console_output.configure(state="disabled")

    def write_to_console(self, text):
        def _write():
            self.console_output.configure(state="normal")
            self.console_output.insert("end", text)
            self.console_output.see("end")
            self.console_output.configure(state="disabled")

        if hasattr(self, "console_output") and self.console_output.winfo_exists():
            self.console_output.after(0, _write)

    def _execute_command_threaded(
        self, command_list, cwd=None, operation_name="Command"
    ):
        self.status_var.set(f"Running: {operation_name}...")
        self.write_to_console(f"\n--- Executing: {operation_name} ---\n")
        if cwd:
            self.write_to_console(f"Working directory: {cwd}\n")
        self.write_to_console(f"Command: {' '.join(command_list)}\n\n")

        try:
            creation_flags = 0
            if platform.system() == "Windows":
                creation_flags = CREATE_NO_WINDOW

            process = subprocess.Popen(
                command_list,
                cwd=cwd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                universal_newlines=True,
                creationflags=creation_flags,
                bufsize=1,
            )

            def stream_output(pipe, prefix=""):
                for line in iter(pipe.readline, ""):
                    self.write_to_console(prefix + line)
                pipe.close()

            stdout_thread = threading.Thread(
                target=stream_output, args=(process.stdout,)
            )
            stderr_thread = threading.Thread(
                target=stream_output, args=(process.stderr, "ERROR: ")
            )
            stdout_thread.start()
            stderr_thread.start()
            stdout_thread.join()
            stderr_thread.join()
            process.wait()

            self.write_to_console(
                f"\n--- {operation_name} completed with exit code: {process.returncode} ---\n"
            )
            self.status_var.set(
                f"{operation_name} completed (Code: {process.returncode})"
            )
            return process.returncode == 0
        except FileNotFoundError:
            self.write_to_console(
                f"ERROR: Command '{command_list[0]}' not found. Ensure it's in your PATH.\n"
            )
            self.status_var.set(f"Error: {command_list[0]} not found")
            return False
        except Exception as e:
            self.write_to_console(
                f"ERROR executing {operation_name}: {str(e)}\n"
            )
            self.status_var.set(f"{operation_name} failed")
            return False

    def dispatch_command(self, action_key, arg=None):
        if hasattr(self, "console_output") and self.console_output.winfo_exists():
            self.console_output.configure(state="normal")
            self.console_output.delete("1.0", "end")
            self.console_output.configure(state="disabled")

        target_action = None
        op_name = action_key.replace("_", " ").title()

        if action_key == "add_migration" and arg:
            target_action = lambda: self._action_add_migration(arg, op_name)
        elif action_key == "revert_migration" and arg: # New action
            target_action = lambda: self._action_revert_migration(arg, op_name)
        elif action_key == "start_db":
            target_action = lambda: self._action_start_db(op_name)
        elif action_key == "recreate_db":
            target_action = lambda: self._action_recreate_db(op_name)
        elif action_key == "stop_db":
            target_action = lambda: self._action_stop_db(op_name)
        elif action_key == "delete_migrations_folder":
            target_action = (
                lambda: self._action_delete_migrations_folder(op_name)
            )
        elif action_key == "full_reset":
            target_action = lambda: self._action_full_reset(op_name)

        if target_action:
            threading.Thread(target=target_action, daemon=True).start()
        else:
            self.write_to_console(
                f"Unknown action or missing argument: {action_key}\n"
            )
            self.status_var.set("Error: Unknown action")

    # --- UI Action Triggers ---
    def ui_action_add_migration(self):
        dialog = MigrationDialog(self, "Add Migration", "Enter migration name:")
        self.wait_window(dialog)
        if dialog.user_input:
            self.dispatch_command("add_migration", dialog.user_input)

    def ui_action_revert_migration(self): # New UI action
        dialog = MigrationDialog(
            self,
            "Revert to Migration",
            "Target migration name (or '0' to revert all):"
        )
        self.wait_window(dialog)
        if dialog.user_input is not None: # Check for None to allow '0'
            self.dispatch_command("revert_migration", dialog.user_input)


    def ui_action_delete_migrations(self):
        confirm_dialog = ConfirmDialog(
            self,
            "Confirm Delete",
            "Are you sure you want to delete the Migrations folder?",
        )
        self.wait_window(confirm_dialog)
        if confirm_dialog.result:
            self.dispatch_command("delete_migrations_folder")

    def ui_action_full_reset(self):
        confirm_dialog = ConfirmDialog(
            self,
            "Confirm Full Reset",
            "This will:\n1. Delete the Migrations folder.\n2. Recreate the database (down, volume prune, up).\n3. Add an 'Init' migration.\n\nThis is destructive. Continue?",
        )
        self.wait_window(confirm_dialog)
        if confirm_dialog.result:
            self.dispatch_command("full_reset")

    # --- Specific Actions ---
    def _action_add_migration(self, migration_name, op_name):
        if not migration_name.strip():
            self.write_to_console("Migration name cannot be empty.\n")
            self.status_var.set("Error: Empty migration name")
            return
        cmd = ["dotnet", "ef", "migrations", "add", migration_name]
        self._execute_command_threaded(
            cmd, cwd=INFRASTRUCTURE_PROJECT_DIR, operation_name=op_name
        )

    def _action_revert_migration(self, target_migration_name, op_name): # New action implementation
        if not target_migration_name.strip(): # "0" is a valid input, strip handles spaces
            self.write_to_console("Target migration name cannot be empty.\n")
            self.status_var.set("Error: Empty target migration name")
            return
        # The command is 'dotnet ef database update <MIGRATION_NAME>'
        # <MIGRATION_NAME> is the name of the last migration to apply.
        # '0' means revert all migrations.
        cmd = ["dotnet", "ef", "database", "update", target_migration_name]
        self._execute_command_threaded(
            cmd, cwd=INFRASTRUCTURE_PROJECT_DIR, operation_name=op_name
        )

    def _action_start_db(self, op_name):
        cmd = ["docker", "compose", "-f", DOCKER_COMPOSE_FULL_PATH, "up", "-d"]
        self._execute_command_threaded(
            cmd, cwd=PROJECT_ROOT, operation_name=op_name
        )

    def _action_stop_db(self, op_name):
        cmd = ["docker", "compose", "-f", DOCKER_COMPOSE_FULL_PATH, "down"]
        self._execute_command_threaded(
            cmd, cwd=PROJECT_ROOT, operation_name=op_name
        )

    def _action_recreate_db(self, op_name):
        self.write_to_console("Step 1/3: Stopping database...\n")
        if not self._execute_command_threaded(
            ["docker", "compose", "-f", DOCKER_COMPOSE_FULL_PATH, "down"],
            cwd=PROJECT_ROOT,
            operation_name=f"{op_name} (Down)",
        ):
            self.write_to_console(
                "Failed to stop database. Aborting recreate.\n"
            )
            return

        self.write_to_console("\nStep 2/3: Pruning Docker volumes...\n")
        if not self._execute_command_threaded(
            ["docker", "volume", "prune", "-f"],
            cwd=PROJECT_ROOT,
            operation_name=f"{op_name} (Volume Prune)",
        ):
            self.write_to_console(
                "Failed to prune volumes. Continuing with caution.\n"
            )

        self.write_to_console("\nStep 3/3: Starting database...\n")
        self._execute_command_threaded(
            ["docker", "compose", "-f", DOCKER_COMPOSE_FULL_PATH, "up", "-d"],
            cwd=PROJECT_ROOT,
            operation_name=f"{op_name} (Up)",
        )

    def _action_delete_migrations_folder(self, op_name):
        self.write_to_console(f"Attempting to delete: {MIGRATIONS_FULL_PATH}\n")
        if os.path.exists(MIGRATIONS_FULL_PATH) and os.path.isdir(
            MIGRATIONS_FULL_PATH
        ):
            try:
                shutil.rmtree(MIGRATIONS_FULL_PATH)
                self.write_to_console("Migrations folder deleted successfully.\n")
                self.status_var.set("Migrations folder deleted.")
                return True
            except Exception as e:
                self.write_to_console(
                    f"Error deleting migrations folder: {e}\n"
                )
                self.status_var.set("Error deleting migrations.")
                return False
        else:
            self.write_to_console(
                "Migrations folder not found or is not a directory.\n"
            )
            self.status_var.set("Migrations folder not found.")
            return True

    def _action_full_reset(self, op_name):
        self.write_to_console("--- STARTING FULL PROJECT RESET ---\n")

        self.write_to_console("\nStep 1/3: Deleting migrations folder...\n")
        if not self._action_delete_migrations_folder(
            f"{op_name} (Delete Migrations)"
        ):
            self.write_to_console(
                "Failed to delete migrations folder. Full reset might be incomplete.\n"
            )
        time.sleep(0.5)

        self.write_to_console("\nStep 2/3: Recreating database...\n")
        self._action_recreate_db(f"{op_name} (Recreate DB)")
        self.write_to_console("Waiting for database to initialize...\n")
        time.sleep(5)

        self.write_to_console("\nStep 3/3: Adding 'Init' migration...\n")
        self._action_add_migration("Init", f"{op_name} (Add Init Migration)")

        self.write_to_console("\n--- FULL PROJECT RESET COMPLETED ---\n")
        self.status_var.set("Full project reset completed.")


class MigrationDialog(ctk.CTkToplevel):
    def __init__(self, parent, title, prompt_text):
        super().__init__(parent)
        self.title(title)
        self.geometry("400x180")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.user_input = None # Important: initialize to None

        self.update_idletasks()
        x = (
            (parent.winfo_width() // 2)
            - (self.winfo_width() // 2)
            + parent.winfo_x()
        )
        y = (
            (parent.winfo_height() // 2)
            - (self.winfo_height() // 2)
            + parent.winfo_y()
        )
        self.geometry(f"+{x}+{y}")
        self.create_widgets(prompt_text)

    def create_widgets(self, prompt_text):
        frame = ctk.CTkFrame(self)
        frame.pack(fill="both", expand=True, padx=20, pady=20)
        label = ctk.CTkLabel(
            frame, text=prompt_text, font=ctk.CTkFont(size=14)
        )
        label.pack(pady=(0, 10))
        self.entry = ctk.CTkEntry(
            frame, width=300, height=35, font=ctk.CTkFont(size=14)
        )
        self.entry.pack(pady=(0, 15))
        self.entry.focus_set()

        button_frame = ctk.CTkFrame(frame, fg_color="transparent")
        button_frame.pack(fill="x", pady=(10, 0))
        button_frame.grid_columnconfigure(0, weight=1)
        button_frame.grid_columnconfigure(1, weight=1)
        ok_button = ctk.CTkButton(
            button_frame,
            text="OK",
            command=self.on_ok,
            fg_color="#28a745",
            hover_color="#218838",
        )
        ok_button.grid(row=0, column=0, padx=(0, 5), pady=0, sticky="ew")
        cancel_button = ctk.CTkButton(
            button_frame,
            text="Cancel",
            command=self.on_cancel,
            fg_color="#6c757d",
            hover_color="#5a6268",
        )
        cancel_button.grid(row=0, column=1, padx=(5, 0), pady=0, sticky="ew")
        self.bind("<Return>", lambda event: self.on_ok())
        self.bind("<Escape>", lambda event: self.on_cancel())

    def on_ok(self):
        self.user_input = self.entry.get() # Get raw input, strip later if needed
        # For revert, "0" is valid and shouldn't be stripped to empty if it's just "0"
        # The _action_revert_migration will handle stripping for its check
        if not self.user_input.strip() and self.user_input != "0": # Allow "0" even if it strips to empty
            self.entry.configure(border_color="red")
            self.entry.focus_set()
            return
        self.destroy()

    def on_cancel(self):
        self.user_input = None # Ensure it's None on cancel
        self.destroy()


class ConfirmDialog(ctk.CTkToplevel):
    def __init__(self, parent, title, message):
        super().__init__(parent)
        self.title(title)
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.result = False
        self.create_widgets(message)
        self.update_idletasks()
        x = parent.winfo_x() + (parent.winfo_width() - self.winfo_width()) // 2
        y = parent.winfo_y() + (parent.winfo_height() - self.winfo_height()) // 2
        self.geometry(f"+{x}+{y}")

    def create_widgets(self, message):
        frame = ctk.CTkFrame(self)
        frame.pack(fill="both", expand=True, padx=20, pady=20)
        warning_label = ctk.CTkLabel(
            frame, text="⚠️", font=ctk.CTkFont(size=30)
        )
        warning_label.pack(pady=(0, 10))
        message_label = ctk.CTkLabel(
            frame,
            text=message,
            font=ctk.CTkFont(size=14),
            wraplength=350,
            justify="left",
        )
        message_label.pack(pady=(0, 20))
        button_frame = ctk.CTkFrame(frame, fg_color="transparent")
        button_frame.pack(fill="x")
        button_frame.grid_columnconfigure(0, weight=1)
        button_frame.grid_columnconfigure(1, weight=1)
        yes_button = ctk.CTkButton(
            button_frame,
            text="Yes",
            command=self.on_yes,
            fg_color="#dc3545",
            hover_color="#c82333",
        )
        yes_button.grid(row=0, column=0, padx=(0, 5), pady=0, sticky="ew")
        no_button = ctk.CTkButton(
            button_frame,
            text="No",
            command=self.on_no,
            fg_color="#6c757d",
            hover_color="#5a6268",
        )
        no_button.grid(row=0, column=1, padx=(5, 0), pady=0, sticky="ew")
        no_button.focus_set()
        self.bind("<Escape>", lambda event: self.on_no())

    def on_yes(self):
        self.result = True
        self.destroy()

    def on_no(self):
        self.result = False
        self.destroy()


if __name__ == "__main__":
    app = MigrationManagerGUI()
    app.mainloop()
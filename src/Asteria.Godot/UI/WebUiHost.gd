extends Node

signal message_received(message: String)
signal webui_ready

const WEBVIEW_CLASS := "WebView"
const UI_URL := "res://ui/dist/index.html"
const UI_SOURCE_MANIFEST := "res://ui/dist/source-manifest.txt"
const UI_SOURCE_PATHS := [
	"res://ui/src",
	"res://ui/index.html",
	"res://ui/package.json",
	"res://ui/package-lock.json",
	"res://ui/tsconfig.json",
	"res://ui/vite.config.ts",
	"res://packs/default/data/localization",
]

var _webview: Control
var _loaded := false
var _creation_mode := true
var _mouse_captured := false
var _native_key_capture := false

func _ready() -> void:
	if not _ensure_webui_bundle():
		return

	# The release package carries its own WebView2 Fixed Version runtime.
	# Configure it before WRY instantiates its native WebView2 environment.
	if not _prepare_webview_runtime():
		return

	if not ClassDB.class_exists(WEBVIEW_CLASS):
		push_error("Godot WRY is not installed. Run scripts/install-webui.ps1 and restart Godot.")
		OS.alert("Asteria could not initialize its embedded interface because the WebView component is missing.")
		return

	_webview = ClassDB.instantiate(WEBVIEW_CLASS) as Control
	if _webview == null:
		push_error("Failed to instantiate Godot WRY WebView.")
		OS.alert("Asteria could not create its embedded interface.")
		return

	_webview.name = "WebView"
	_webview.set("full_window_size", true)
	_webview.set("transparent", true)
	_webview.set("forward_input_events", true)
	_webview.set("focused_when_created", false)
	_webview.focus_mode = Control.FOCUS_ALL
	_webview.set("devtools", OS.is_debug_build())
	_webview.set("data_directory", "user://webview")
	_webview.connect("ipc_message", Callable(self, "_on_ipc_message"))
	_webview.connect("page_load_finished", Callable(self, "_on_page_load_finished"))
	add_child(_webview)
	_webview.call("load_url", UI_URL)


func _prepare_webview_runtime() -> bool:
	if OS.get_name() != "Windows":
		return true

	var executable_directory := OS.get_executable_path().get_base_dir()
	var runtime_directory := executable_directory.path_join("WebView2Runtime")
	var browser_executable := runtime_directory.path_join("msedgewebview2.exe")

	# Editor/debug runs may use the developer's installed Evergreen runtime.
	# A Release export must always have the self-contained runtime beside it.
	if not FileAccess.file_exists(browser_executable):
		if OS.is_debug_build():
			return true

		var message := "The bundled WebView2 runtime is missing from: " + runtime_directory
		push_error(message)
		OS.alert("Asteria's bundled interface runtime is missing. Re-extract the complete game package.")
		return false

	var version_file := runtime_directory.path_join("asteria-runtime-version.txt")
	var runtime_version := FileAccess.get_file_as_string(version_file).strip_edges()
	if runtime_version.is_empty():
		push_error("Bundled WebView2 runtime version marker is missing.")
		OS.alert("Asteria's interface runtime is incomplete. Re-extract the complete game package.")
		return false

	# Fixed Version 120+ needs these inherited AppContainer read permissions
	# on unpackaged Win32 applications running on Windows 10.
	var windows_build := int(OS.get_version_info().get("build", 0))
	var needs_appcontainer_acl := windows_build > 0 and windows_build < 22000
	if needs_appcontainer_acl:
		var permission_marker := "user://webview2-fixed-runtime-acl.txt"
		var expected_marker := runtime_directory + "|" + runtime_version
		var permissions_applied := FileAccess.file_exists(permission_marker)
		if permissions_applied:
			permissions_applied = FileAccess.get_file_as_string(permission_marker).strip_edges() == expected_marker
		if not permissions_applied:
			var required_permissions := [
				"*S-1-15-2-2:(OI)(CI)(RX)",
				"*S-1-15-2-1:(OI)(CI)(RX)",
			]
			for permission in required_permissions:
				var output: Array = []
				var exit_code := OS.execute(
					"icacls.exe",
					PackedStringArray([runtime_directory, "/grant", permission]),
					output,
					true
				)
				if exit_code != 0:
					var details := "\n".join(output)
					push_error("Could not prepare WebView2 runtime permissions: " + details)
					OS.alert("Asteria could not prepare its bundled interface runtime. Extract the game into a folder writable by your Windows user, then try again.")
					return false

			var marker := FileAccess.open(permission_marker, FileAccess.WRITE)
			if marker == null:
				push_error("Could not persist WebView2 runtime permission marker.")
				return false
			marker.store_string(expected_marker)
			marker.close()

	OS.set_environment("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", runtime_directory)
	print("WebView2 Fixed Version runtime: " + runtime_version)
	return true


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_WINDOW_FOCUS_IN:
		call_deferred("_restore_keyboard_focus")

func _input(event: InputEvent) -> void:
	# WRY is a native child webview layered over the game window. A mouse
	# interaction may give that child keyboard focus, so restore game focus
	# immediately after the click. Mouse events themselves are still forwarded.
	if _mouse_captured and event is InputEventMouseButton and event.pressed:
		call_deferred("_focus_game")

func post_message(message: String) -> void:
	if _webview != null and _loaded:
		_webview.call("post_message", message)

func set_webui_visible(visible: bool) -> void:
	if _webview != null:
		_webview.call("set_visible", visible)
		if visible:
			call_deferred("_restore_keyboard_focus")

func set_creation_mode(creating: bool) -> void:
	_creation_mode = creating
	call_deferred("_restore_keyboard_focus")

func set_mouse_captured(captured: bool) -> void:
	_mouse_captured = captured
	call_deferred("_restore_keyboard_focus")
	# The game.mouse_capture bridge event already drives
	# html.mouse-captured in App.tsx. Global CSS hides the cursor,
	# including descendants, without O(DOM size) inline mutations.
	# Do not eval the entire browser tree on each capture change.

func _on_page_load_finished(_url: String) -> void:
	_loaded = true
	_restore_keyboard_focus()
	webui_ready.emit()

func _on_ipc_message(message: String) -> void:
	message_received.emit(message)
	call_deferred("_restore_keyboard_focus")

func set_native_key_capture(active: bool) -> void:
	_native_key_capture = active
	call_deferred("_restore_keyboard_focus")

func focus_chat_input() -> void:
	# The capture-loss handler also queues a focus handoff. Complete that
	# handoff first so the chat field is not focused while Godot still owns
	# keyboard input. DOM element focus remains React's responsibility.
	call_deferred("_focus_chat_input_after_handoff")

func _focus_chat_input_after_handoff() -> void:
	if _webview == null or not _loaded or _mouse_captured or _creation_mode or not get_window().has_focus():
		return
	_focus_webview()
	call_deferred("_acknowledge_chat_focus")

func _acknowledge_chat_focus() -> void:
	if _webview == null or not _loaded or _mouse_captured or _creation_mode or not get_window().has_focus():
		return
	post_message('{"type":"game.chat.focus_input","payload":{}}')

func webui_has_focus() -> bool:
	return _webview != null and _webview.has_focus()

func _restore_keyboard_focus() -> void:
	# A deferred focus restore must not reactivate the game after Alt+Tab.
	if _webview == null or not _loaded or not get_window().has_focus():
		return
	if _native_key_capture or (_mouse_captured and not _creation_mode):
		_focus_game()
	else:
		_focus_webview()

func _focus_webview() -> void:
	# grab_focus() only selects the Godot Control; focus() transfers OS
	# keyboard focus into the native WRY WebView (and its HTML input).
	_webview.grab_focus()
	_webview.call("focus")

func _focus_game() -> void:
	if _webview != null and get_window().has_focus():
		_webview.call("focus_parent")


func _ensure_webui_bundle() -> bool:
	if not OS.is_debug_build():
		if FileAccess.file_exists(UI_URL):
			return true

		push_error("WebUI bundle is missing at %s. Build ui/dist before running a release build." % UI_URL)
		return false

	if not _webui_bundle_is_stale():
		return true

	var ui_directory := ProjectSettings.globalize_path("res://ui")
	var output: Array = []
	var exit_code := 0

	if OS.get_name() == "Windows":
		exit_code = OS.execute(
			"cmd.exe",
			PackedStringArray([
				"/C",
				"npm",
				"--prefix",
				ui_directory,
				"run",
				"build",
			]),
			output,
			true
		)
	else:
		exit_code = OS.execute(
			"npm",
			PackedStringArray([
				"--prefix",
				ui_directory,
				"run",
				"build",
			]),
			output,
			true
		)

	if exit_code != 0:
		push_error(
			"WebUI build failed (exit code %d). Inspect the npm output below; " +
			"check Node.js/npm only if the command could not run.\n%s"
			% [exit_code, "\n".join(output)]
		)
		return false

	if not FileAccess.file_exists(UI_URL):
		push_error("WebUI build completed without producing %s." % UI_URL)
		return false

	return true


func _webui_bundle_is_stale() -> bool:
	# Timestamps cannot prove bundle freshness after git checkouts or same-second
	# file updates. The Vite build writes hashes for every source file instead.
	if not FileAccess.file_exists(UI_URL) or not FileAccess.file_exists(UI_SOURCE_MANIFEST):
		return true

	var expected := {}
	var manifest := FileAccess.get_file_as_string(UI_SOURCE_MANIFEST)
	for line in manifest.split("\n", false):
		var columns := line.split("\t", false)
		if columns.size() != 2:
			return true
		expected["res://" + columns[0]] = columns[1]

	var source_files: Array[String] = []
	for root in UI_SOURCE_PATHS:
		_collect_ui_sources(root, source_files)
	if source_files.size() != expected.size():
		return true

	for source_path in source_files:
		if not expected.has(source_path):
			return true
		if FileAccess.get_sha256(source_path) != expected[source_path]:
			return true
	return false


func _collect_ui_sources(path: String, files: Array[String]) -> void:
	if FileAccess.file_exists(path):
		files.append(path)
		return

	var directory := DirAccess.open(path)
	if directory == null:
		return

	directory.list_dir_begin()
	var entry := directory.get_next()
	while entry != "":
		var child := path.path_join(entry)
		if directory.current_is_dir():
			_collect_ui_sources(child, files)
		else:
			files.append(child)
		entry = directory.get_next()
	directory.list_dir_end()

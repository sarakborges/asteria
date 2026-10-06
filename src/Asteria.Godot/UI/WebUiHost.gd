extends Node

signal message_received(message: String)
signal webui_ready

const WEBVIEW_CLASS := "WebView"
const UI_URL := "res://ui/dist/index.html"
const UI_SOURCE_PATHS := [
	"res://ui/src",
	"res://ui/index.html",
	"res://ui/package.json",
	"res://ui/package-lock.json",
	"res://ui/tsconfig.json",
	"res://ui/vite.config.ts",
]

var _webview: Control
var _loaded := false

func _ready() -> void:
	if not _ensure_webui_bundle():
		return

	if not ClassDB.class_exists(WEBVIEW_CLASS):
		push_error("Godot WRY is not installed. Run scripts/install-webui.ps1 and restart Godot.")
		return

	_webview = ClassDB.instantiate(WEBVIEW_CLASS) as Control
	if _webview == null:
		push_error("Failed to instantiate Godot WRY WebView.")
		return

	_webview.name = "WebView"
	_webview.set("full_window_size", true)
	_webview.set("transparent", true)
	_webview.set("forward_input_events", true)
	_webview.set("focused_when_created", false)
	_webview.set("devtools", OS.is_debug_build())
	_webview.set("data_directory", "user://webview")
	_webview.connect("ipc_message", Callable(self, "_on_ipc_message"))
	_webview.connect("page_load_finished", Callable(self, "_on_page_load_finished"))
	add_child(_webview)
	_webview.call("load_url", UI_URL)

func _input(event: InputEvent) -> void:
	# WRY is a native child webview layered over the game window. A mouse
	# interaction may give that child keyboard focus, so restore game focus
	# immediately after the click. Mouse events themselves are still forwarded.
	if event is InputEventMouseButton and event.pressed:
		call_deferred("_focus_game")

func post_message(message: String) -> void:
	if _webview != null and _loaded:
		_webview.call("post_message", message)

func set_webui_visible(visible: bool) -> void:
	if _webview != null:
		_webview.call("set_visible", visible)
		if visible:
			call_deferred("_focus_game")

func set_mouse_captured(captured: bool) -> void:
	if _webview == null or not _loaded:
		return

	var cursor_value := "none" if captured else ""
	var script := "document.documentElement.style.cursor='%s';" % cursor_value
	script += "if(document.body){document.body.style.cursor='%s';}" % cursor_value
	script += "document.querySelectorAll('*').forEach(function(e){e.style.cursor='%s';});" % cursor_value
	_webview.call("eval", script)

func _on_page_load_finished(_url: String) -> void:
	_loaded = true
	_focus_game()
	webui_ready.emit()

func _on_ipc_message(message: String) -> void:
	message_received.emit(message)
	call_deferred("_focus_game")

func _focus_game() -> void:
	if _webview != null:
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
			"Failed to build WebUI automatically. Ensure Node.js/npm is installed.\n%s"
			% "\n".join(output)
		)
		return false

	if not FileAccess.file_exists(UI_URL):
		push_error("WebUI build completed without producing %s." % UI_URL)
		return false

	return true


func _webui_bundle_is_stale() -> bool:
	if not FileAccess.file_exists(UI_URL):
		return true

	var bundle_modified := FileAccess.get_modified_time(UI_URL)

	for source_path in UI_SOURCE_PATHS:
		if _latest_modified_time(source_path) > bundle_modified:
			return true

	return false


func _latest_modified_time(path: String) -> int:
	if FileAccess.file_exists(path):
		return FileAccess.get_modified_time(path)

	var directory := DirAccess.open(path)
	if directory == null:
		return 0

	var latest := 0
	directory.list_dir_begin()

	var entry := directory.get_next()
	while entry != "":
		var child_path := path.path_join(entry)
		if directory.current_is_dir():
			latest = max(latest, _latest_modified_time(child_path))
		else:
			latest = max(latest, FileAccess.get_modified_time(child_path))

		entry = directory.get_next()

	directory.list_dir_end()
	return latest

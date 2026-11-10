extends Node

signal message_received(message: String)
signal webui_ready
signal diagnostic_event(level: String, event_name: String, details: String)

const WEBVIEW_CLASS := "WebView"
const UI_URL := "res://webui-dist/index.html"
# Keep the registered custom protocol in the URL passed to WRY. On Windows,
# WRY itself rewrites res:// URLs for WebView2 and routes them back through
# the res handler. Pre-rewriting to http:// bypasses that custom-protocol path.
const UI_SOURCE_MANIFEST := "res://webui-dist/source-manifest.txt"
const UI_SOURCE_PATHS := [
	"res://ui/src",
	"res://ui/index.html",
	"res://ui/package.json",
	"res://ui/package-lock.json",
	"res://ui/tsconfig.json",
	"res://ui/vite.config.ts",
	"res://packs/default/data/localization",
	"res://packs/default/ui",
]
const UI_STARTUP_TIMEOUT_SECONDS := 20.0

var _webview: Control
var _fallback_layer: CanvasLayer
var _loaded := false
var _page_load_started := false
var _page_load_finished := false
var _bridge_ready := false
var _creation_mode := true
var _mouse_captured := false
var _native_key_capture := false
var _runtime_version := "system"
var _diagnostic_file: FileAccess


func _ready() -> void:
	# Main connects diagnostic_event during its own _ready(). Child _ready()
	# methods run first, so defer startup or early WebUI failures are lost.
	call_deferred("_initialize_webui")


func _initialize_webui() -> void:
	_open_diagnostic_log()
	_diagnostic("INFO", "webui.host.ready", "os=%s url=%s" % [OS.get_name(), UI_URL])

	if not _ensure_webui_bundle():
		_diagnostic("ERROR", "webui.bundle.missing", "path=%s" % UI_URL)
		_show_startup_failure("O pacote da interface não foi encontrado.")
		return
	_diagnostic("INFO", "webui.bundle.ready", "path=%s" % UI_URL)

	# The release package carries its own WebView2 Fixed Version runtime.
	# Configure it before WRY instantiates its native WebView2 environment.
	if not _prepare_webview_runtime():
		_show_startup_failure("O runtime local da interface não pôde ser preparado.")
		return

	if not ClassDB.class_exists(WEBVIEW_CLASS):
		_diagnostic("ERROR", "webui.extension.missing", "class=%s" % WEBVIEW_CLASS)
		_show_startup_failure("O componente nativo WebView não foi carregado.")
		return

	_webview = ClassDB.instantiate(WEBVIEW_CLASS) as Control
	if _webview == null:
		_diagnostic("ERROR", "webui.extension.instantiate_failed", "class=%s" % WEBVIEW_CLASS)
		_show_startup_failure("O componente nativo WebView não pôde ser criado.")
		return

	_webview.name = "WebView"
	_webview.set("full_window_size", true)
	_webview.set("url", UI_URL)
	_webview.set("transparent", true)
	_webview.set("forward_input_events", true)
	_webview.set("focused_when_created", false)
	_webview.focus_mode = Control.FOCUS_ALL
	_webview.set("devtools", OS.is_debug_build())
	_webview.set("data_directory", "user://webview")
	_webview.connect("ipc_message", Callable(self, "_on_ipc_message"))
	_webview.connect("page_load_started", Callable(self, "_on_page_load_started"))
	_webview.connect("page_load_finished", Callable(self, "_on_page_load_finished"))

	# Set the local res:// URL before entering the tree. WRY then creates
	# the native browser directly on the game UI instead of first navigating
	# to its upstream GitHub demo URL and replacing it afterward.
	_diagnostic(
		"INFO",
		"webui.webview.create_requested",
		"url=%s viewport=%s fixed_runtime=%s" % [
			UI_URL,
			get_viewport().get_visible_rect().size,
			_runtime_version,
		]
	)
	add_child(_webview)
	_diagnostic("INFO", "webui.webview.created", "visible=%s" % _webview.is_visible_in_tree())

	get_tree().create_timer(UI_STARTUP_TIMEOUT_SECONDS).timeout.connect(
		Callable(self, "_on_startup_timeout")
	)


func _portable_root() -> String:
	var configured_root := OS.get_environment("ASTERIA_PORTABLE_ROOT")
	if not configured_root.is_empty():
		return configured_root
	if OS.has_feature("editor"):
		return ProjectSettings.globalize_path("res://")
	return OS.get_executable_path().get_base_dir()


func _open_diagnostic_log() -> void:
	var directory := _portable_root().path_join("logs")
	var error := DirAccess.make_dir_recursive_absolute(directory)
	if error != OK:
		print("ASTERIA_WEBUI_DIAGNOSTICS directory_error=%s path=%s" % [error, directory])
		return

	var log_path := directory.path_join("webui-startup.log")
	_diagnostic_file = FileAccess.open_absolute(log_path, FileAccess.WRITE)
	if _diagnostic_file == null:
		print("ASTERIA_WEBUI_DIAGNOSTICS open_error=%s path=%s" % [
			FileAccess.get_open_error(),
			log_path,
		])
		return
	_diagnostic_file.store_line("session.start os=%s pid=%s" % [OS.get_name(), OS.get_process_id()])
	_diagnostic_file.flush()


func _diagnostic(level: String, event_name: String, details: String = "") -> void:
	var line := "%s %s %s" % [level, event_name, details]
	print("ASTERIA_WEBUI_DIAGNOSTICS " + line)
	if _diagnostic_file != null:
		_diagnostic_file.store_line(line)
		_diagnostic_file.flush()
	diagnostic_event.emit(level, event_name, details)


func _prepare_webview_runtime() -> bool:
	if OS.get_name() != "Windows":
		_runtime_version = "platform-default"
		_diagnostic("INFO", "webui.runtime.platform_default", "os=%s" % OS.get_name())
		return true

	var executable_directory := OS.get_executable_path().get_base_dir()
	var runtime_directory := executable_directory.path_join("WebView2Runtime")
	var browser_executable := runtime_directory.path_join("msedgewebview2.exe")

	# Editor/debug runs may use the developer's installed Evergreen runtime.
	# A Release export must always have the self-contained runtime beside it.
	if not FileAccess.file_exists(browser_executable):
		if OS.is_debug_build():
			_runtime_version = "evergreen-debug"
			_diagnostic("WARN", "webui.runtime.fixed_missing", "debug_build=true using_system_runtime=true")
			return true

		_diagnostic("ERROR", "webui.runtime.fixed_missing", "path=%s" % browser_executable)
		return false

	var version_file := runtime_directory.path_join("asteria-runtime-version.txt")
	var runtime_version := FileAccess.get_file_as_string(version_file).strip_edges()
	if runtime_version.is_empty():
		_diagnostic("ERROR", "webui.runtime.version_marker_missing", "path=%s" % version_file)
		return false

	# Fixed Version 120+ needs these inherited AppContainer read permissions
	# on unpackaged Win32 applications. Granting them is harmless on Windows 11
	# and required on Windows 10.
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
				_diagnostic("ERROR", "webui.runtime.acl_failed", "permission=%s output=%s" % [permission, details])
				return false

		var marker := FileAccess.open(permission_marker, FileAccess.WRITE)
		if marker == null:
			_diagnostic("ERROR", "webui.runtime.marker_write_failed", "path=%s" % permission_marker)
			return false
		marker.store_string(expected_marker)
		marker.close()

	# Keep the loader environment in native Windows path format. The WRY
	# native layer normalizes this again before calling the WebView2 API.
	var native_runtime_directory := runtime_directory.replace("/", "\\")
	OS.set_environment("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", native_runtime_directory)
	_runtime_version = runtime_version
	_diagnostic(
		"INFO",
		"webui.runtime.fixed_selected",
		"version=%s path=%s env=%s" % [
			runtime_version,
			native_runtime_directory,
			OS.get_environment("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"),
		]
	)
	return true


func _on_page_load_started(url: String) -> void:
	_page_load_started = true
	_diagnostic("INFO", "webui.page.load_started", "url=%s" % url)


func _on_page_load_finished(url: String) -> void:
	_page_load_finished = true
	_loaded = true
	_diagnostic("INFO", "webui.page.load_finished", "url=%s" % url)
	_restore_keyboard_focus()
	webui_ready.emit()


func _on_ipc_message(message: String) -> void:
	var decoded: Variant = JSON.parse_string(message)
	if decoded is Dictionary:
		var message_type := String(decoded.get("type", ""))
		if message_type == "ui.ready":
			_bridge_ready = true
			_loaded = true
			_dismiss_startup_failure()
		elif message_type == "ui.assets.ready":
			var payload: Variant = decoded.get("payload", {})
			if payload is Dictionary:
				var font_loaded := bool(payload.get("font_loaded", false))
				var logo_loaded := bool(payload.get("logo_loaded", false))
				var level := "INFO" if font_loaded and logo_loaded else "ERROR"
				_diagnostic(level, "webui.assets.ready", "payload=%s" % JSON.stringify(payload))
			else:
				_diagnostic("ERROR", "webui.assets.ready", "payload_invalid=true")
		elif message_type == "ui.dom.ready":
			_diagnostic("INFO", "webui.dom.ready", "payload=%s" % JSON.stringify(decoded.get("payload", {})))

	message_received.emit(message)
	call_deferred("_restore_keyboard_focus")


func _on_startup_timeout() -> void:
	if _bridge_ready or _fallback_layer != null:
		return

	var details := "webview_created=%s page_load_started=%s page_load_finished=%s ui_ready=%s runtime=%s" % [
		_webview != null,
		_page_load_started,
		_page_load_finished,
		_bridge_ready,
		_runtime_version,
	]
	_diagnostic("ERROR", "webui.startup.timeout", details)
	_show_startup_failure(details)


func _show_startup_failure(details: String) -> void:
	if _fallback_layer != null:
		return

	if _webview != null:
		_webview.call("set_visible", false)

	_fallback_layer = CanvasLayer.new()
	_fallback_layer.name = "WebUiStartupFailure"
	_fallback_layer.layer = 100
	add_child(_fallback_layer)

	var full_screen := Control.new()
	full_screen.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_fallback_layer.add_child(full_screen)

	var background := ColorRect.new()
	background.color = Color(0.055, 0.065, 0.085, 1.0)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	full_screen.add_child(background)

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	full_screen.add_child(center)

	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(680, 290)
	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = Color(0.09, 0.105, 0.14, 1.0)
	panel_style.border_color = Color(0.28, 0.32, 0.40, 1.0)
	panel_style.set_border_width_all(1)
	panel_style.set_corner_radius_all(10)
	panel_style.set_content_margin_all(24.0)
	panel.add_theme_stylebox_override("panel", panel_style)
	center.add_child(panel)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 14)
	panel.add_child(content)

	var title := Label.new()
	title.text = "A interface do Asteria não iniciou"
	title.add_theme_font_size_override("font_size", 26)
	content.add_child(title)

	var message := Label.new()
	message.text = (
		"O jogo iniciou, mas a interface WebView/React não confirmou a inicialização.\n\n" +
		details +
		"\n\nO diagnóstico foi salvo em: %s" % _portable_root().path_join("logs")
	)
	message.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	content.add_child(message)

	var close_button := Button.new()
	close_button.text = "Fechar Asteria"
	close_button.size_flags_horizontal = Control.SIZE_SHRINK_END
	close_button.pressed.connect(_on_startup_failure_close)
	content.add_child(close_button)


func _dismiss_startup_failure() -> void:
	if _fallback_layer != null:
		_fallback_layer.queue_free()
		_fallback_layer = null
	if _webview != null:
		_webview.call("set_visible", true)
	_diagnostic("INFO", "webui.startup.recovered", "ui_ready=true")


func _on_startup_failure_close() -> void:
	get_tree().quit()


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

		push_error("WebUI bundle is missing at %s. Build WebUI before running a release build." % UI_URL)
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

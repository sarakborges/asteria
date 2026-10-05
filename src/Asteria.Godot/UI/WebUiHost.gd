extends Node

signal message_received(message: String)
signal webui_ready

const WEBVIEW_CLASS := "WebView"
const UI_URL := "res://ui/dist/index.html"

var _webview: Control
var _loaded := false

func _ready() -> void:
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
	_webview.set("devtools", OS.is_debug_build())
	_webview.set("data_directory", "user://webview")
	_webview.connect("ipc_message", Callable(self, "_on_ipc_message"))
	_webview.connect("page_load_finished", Callable(self, "_on_page_load_finished"))
	add_child(_webview)
	_webview.call("load_url", UI_URL)

func post_message(message: String) -> void:
	if _webview != null and _loaded:
		_webview.call("post_message", message)

func set_webui_visible(visible: bool) -> void:
	if _webview != null:
		_webview.call("set_visible", visible)

func _on_page_load_finished(_url: String) -> void:
	_loaded = true
	webui_ready.emit()

func _on_ipc_message(message: String) -> void:
	message_received.emit(message)

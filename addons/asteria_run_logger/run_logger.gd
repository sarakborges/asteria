@tool
extends EditorPlugin

const LOG_DIRECTORY := "res://logs"
const ACTIVE_LOG := "res://logs/godot.log"

var _was_playing := false
var _run_stamp := ""

func _enter_tree() -> void:
	_ensure_log_directory()
	_was_playing = EditorInterface.is_playing_scene()
	set_process(true)

func _exit_tree() -> void:
	set_process(false)

func _process(_delta: float) -> void:
	var is_playing := EditorInterface.is_playing_scene()

	if is_playing and not _was_playing:
		_run_stamp = _make_timestamp()
		print("[Asteria] Run started: %s" % _run_stamp)
	elif not is_playing and _was_playing:
		call_deferred("_archive_finished_run")

	_was_playing = is_playing

func _archive_finished_run() -> void:
	if _run_stamp.is_empty():
		return

	# The game process has already stopped at this point. Wait two editor frames
	# so the OS has time to close and flush the active Godot log file.
	await get_tree().process_frame
	await get_tree().process_frame

	if not FileAccess.file_exists(ACTIVE_LOG):
		push_warning("[Asteria] No active Godot log found at %s" % ACTIVE_LOG)
		_run_stamp = ""
		return

	var source := FileAccess.open(ACTIVE_LOG, FileAccess.READ)
	if source == null:
		push_error("[Asteria] Could not read %s" % ACTIVE_LOG)
		_run_stamp = ""
		return

	var bytes := source.get_buffer(source.get_length())
	source.close()

	var archive_path := "%s/run-%s.log" % [LOG_DIRECTORY, _run_stamp]
	var archive := FileAccess.open(archive_path, FileAccess.WRITE)
	if archive == null:
		push_error("[Asteria] Could not create %s" % archive_path)
		_run_stamp = ""
		return

	archive.store_buffer(bytes)
	archive.close()

	print("[Asteria] Run log archived: %s" % archive_path)
	_run_stamp = ""

func _ensure_log_directory() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(LOG_DIRECTORY))

func _make_timestamp() -> String:
	var timestamp := Time.get_datetime_string_from_system(false, true)
	timestamp = timestamp.replace(":", "-").replace(" ", "_")
	return timestamp

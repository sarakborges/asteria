@tool
extends EditorPlugin

const LOG_DIRECTORY := "res://logs"

func _build() -> bool:
	var absolute_log_directory := ProjectSettings.globalize_path(LOG_DIRECTORY)
	DirAccess.make_dir_recursive_absolute(absolute_log_directory)

	var timestamp := Time.get_datetime_string_from_system(false, true)
	timestamp = timestamp.replace(":", "-").replace(" ", "_")

	var log_path := "logs/run-%s.log" % timestamp
	ProjectSettings.set_setting("editor/run/main_run_args", '--log-file "%s"' % log_path)

	print("[Asteria] Run log: %s" % log_path)
	return true

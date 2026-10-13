extends SceneTree

# Native smoke for the interfaces that TypeScript and dotnet builds cannot
# exercise: Godot resource loading, shader include resolution, and packaged
# scene/script references. No WebView is instantiated in headless CI.
const SHADERS := [
	"res://shaders/voxel_object.gdshader",
	"res://shaders/voxel_object_noshadow.gdshader",
	"res://shaders/voxel_terrain_opaque.gdshader",
	"res://shaders/voxel_terrain_cutout.gdshader",
	"res://shaders/voxel_terrain_translucent.gdshader",
]

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var failed := false
	for path in SHADERS:
		var shader := ResourceLoader.load(path)
		if not shader is Shader:
			push_error("Native smoke: failed to load shader " + path)
			failed = true
			continue
		if shader.code.is_empty():
			push_error("Native smoke: shader is empty " + path)
			failed = true
		else:
			print("Native shader loaded: ", path)

	var object_shader := ResourceLoader.load(SHADERS[0]) as Shader
	if object_shader != null:
		var surface := ShaderMaterial.new()
		surface.shader = object_shader
		var image := Image.create_empty(1, 1, false, Image.FORMAT_RGBA8)
		image.fill(Color.WHITE)
		surface.set_shader_parameter("object_texture",
			ImageTexture.create_from_image(image))
		surface.set_shader_parameter("model_albedo_color", Color(0.8, 0.7, 0.6, 0.3))
		surface.set_shader_parameter("preview_mode", true)
		surface.set_shader_parameter("preview_tint", Vector3.ONE)
		var uniforms: Array = object_shader.get_shader_uniform_list()
		for expected in ["object_texture", "model_albedo_color",
			"alpha_cutoff", "preview_mode", "preview_tint"]:
			if not uniforms.any(func(value): return value.name == expected):
				push_error("Native smoke: shader uniform missing: " + expected)
				failed = true


	# The actual Object model loader uses these in-memory GLB APIs. Exercise
	# the same serializer/importer without relying on a pack-side .import file.
	var model_root := Node3D.new()
	var model_part := MeshInstance3D.new()
	model_part.mesh = BoxMesh.new()
	model_root.add_child(model_part)
	var gltf := GLTFDocument.new()
	var encoded := GLTFState.new()
	if gltf.append_from_scene(model_root, encoded) != OK:
		push_error("Native smoke: could not serialize static GLB model")
		failed = true
	else:
		var bytes := gltf.generate_buffer(encoded)
		var decoded := GLTFState.new()
		if bytes.is_empty() or gltf.append_from_buffer(bytes, "", decoded) != OK:
			push_error("Native smoke: could not decode static GLB model")
			failed = true
		else:
			var imported := gltf.generate_scene(decoded)
			if imported == null:
				push_error("Native smoke: decoded GLB has no scene")
				failed = true
			else:
				var found_mesh := false
				for child in imported.get_children():
					if child is MeshInstance3D and child.mesh != null:
						found_mesh = true
				if not found_mesh:
					push_error("Native smoke: GLB lost its mesh")
					failed = true
				imported.free()
	model_root.free()

	var scene := ResourceLoader.load("res://scenes/main.tscn")
	if not scene is PackedScene:
		push_error("Native smoke: main scene cannot load")
		failed = true
	else:
		var instance: Node = scene.instantiate()
		if instance == null:
			push_error("Native smoke: main scene cannot instantiate")
			failed = true
		else:
			if instance.get_script() == null:
				push_error("Native smoke: main scene's C# script was not loaded")
				failed = true
			if instance.get_node_or_null("WebUi") == null:
				push_error("Native smoke: WebUI bridge node missing")
				failed = true
			instance.free()

	if not failed:
		print("ASTERIA_NATIVE_PRESENTATION_SMOKE_OK")
	quit(1 if failed else 0)

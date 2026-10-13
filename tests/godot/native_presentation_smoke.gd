extends SceneTree

# Native smoke for the interfaces that TypeScript and dotnet builds cannot
# exercise: Godot resource loading, shader include resolution, and packaged
# scene/script references. No WebView is instantiated in headless CI.
const SHADERS := [
	"res://shaders/voxel_object.gdshader",
	"res://shaders/voxel_object_shadow_overlay.gdshader",
	"res://shaders/voxel_terrain_opaque.gdshader",
	"res://shaders/voxel_terrain_cutout.gdshader",
	"res://shaders/voxel_terrain_translucent.gdshader",
	"res://shaders/voxel_terrain_opaque_noshadow.gdshader",
	"res://shaders/voxel_terrain_cutout_noshadow.gdshader",
	"res://shaders/voxel_terrain_translucent_noshadow.gdshader",
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

	# Native import and shader-uniform resolution for every terrain mode.
	# Graphical shadow pixels still require an actual Godot/WRY device run.
	for path in SHADERS:
		if path.contains("voxel_terrain"):
			var terrain_shader := ResourceLoader.load(path) as Shader
			if terrain_shader == null or terrain_shader.get_shader_uniform_list().is_empty():
				push_error("Native smoke: terrain shader failed to resolve uniforms " + path)
				failed = true

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


	# A shadow receiver's transparent second pass must keep the underlying
	# voxel-colored material. Headless smoke checks the native shader contract.
	var shadow_shader := ResourceLoader.load(SHADERS[1]) as Shader
	if shadow_shader != null:
		if not shadow_shader.code.contains("shadow_to_opacity"):
			push_error("Native smoke: Object shadow overlay is not shadow-sensitive")
			failed = true
		var shadow_uniforms: Array = shadow_shader.get_shader_uniform_list()
		for expected in ["object_texture", "alpha_cutoff", "alpha_multiplier"]:
			if not shadow_uniforms.any(func(value): return value.name == expected):
				push_error("Native smoke: Object shadow uniform missing: " + expected)
				failed = true
		var overlay := ShaderMaterial.new()
		overlay.shader = shadow_shader
		var object_base := ShaderMaterial.new()
		object_base.shader = object_shader
		object_base.next_pass = overlay
		if object_base.next_pass != overlay:
			push_error("Native smoke: Object shadow NextPass not assigned")
			failed = true
	var shadow_global = ProjectSettings.get_setting("shader_globals/object_sun_shadow_strength", null)
	if not shadow_global is Dictionary or shadow_global.get("type", "") != "float":
		push_error("Native smoke: Object solar shadow global not registered")
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

	# Godot's GLTFDocument material conversion can change the original
	# mask/culling semantics. The player presentation explicitly applies the
	# source glTF contract, which must stay stable across art updates.
	var player_path := ProjectSettings.globalize_path(
		"res://packs/default/resources/models/entities/player/player.glb")
	var player_bytes := FileAccess.get_file_as_bytes(player_path)
	var player_gltf := GLTFDocument.new()
	var player_state := GLTFState.new()
	if player_bytes.is_empty() or player_gltf.append_from_buffer(
		player_bytes, "", player_state) != OK:
		push_error("Native smoke: could not import authored player GLB")
		failed = true
	else:
		var player_scene := player_gltf.generate_scene(player_state)
		if player_scene == null:
			push_error("Native smoke: player GLB generated no scene")
			failed = true
		else:
			var pending: Array[Node] = [player_scene]
			var skin_material: BaseMaterial3D = null
			while not pending.is_empty():
				var node: Node = pending.pop_back()
				if node is MeshInstance3D and node.name == "HeadMesh":
					skin_material = (node as MeshInstance3D).get_active_material(0) as BaseMaterial3D
				for child in node.get_children():
					pending.append(child)
			if skin_material == null:
				push_error("Native smoke: imported player skin material is missing")
				failed = true
			player_scene.free()
		# The first glTF material is shared by every player body cuboid.
		# The player GLB explicitly authors alphaCutoff=0.1.
		var chunk_size: int = player_bytes.decode_u32(12)
		if chunk_size > player_bytes.size() - 20:
			push_error("Native smoke: player GLB JSON chunk is truncated")
			failed = true
		else:
			var gltf_json: Variant = JSON.parse_string(
				player_bytes.slice(20, 20 + chunk_size).get_string_from_utf8())
			if not gltf_json is Dictionary:
				push_error("Native smoke: player GLB JSON is invalid")
				failed = true
			else:
				var materials: Array = (gltf_json as Dictionary).get("materials", [])
				if materials.is_empty() or not (materials[0] is Dictionary):
					push_error("Native smoke: player GLB material missing")
					failed = true
				else:
					var material: Dictionary = materials[0]
					if (material.get("alphaMode", "OPAQUE") != "MASK"
						or material.get("doubleSided", false) != true
						or not is_equal_approx(float(material.get("alphaCutoff", -1.0)), 0.1)):
						push_error("Native smoke: player GLB alpha/double-sided policy drifted: " + JSON.stringify(material))
						failed = true

	# Imported creature GLB material names are the authored overrides'
	# lookup keys. Exercise them natively rather than trusting JSON alone.
	# Accent materials do not specify texture replacements, but must still be
	# resolvable for their independent unlit policy.
	var creature_path := ProjectSettings.globalize_path(
		"res://packs/default/resources/models/creatures/slime_hydro/slime_hydro.glb")
	var creature_bytes := FileAccess.get_file_as_bytes(creature_path)
	var creature_gltf := GLTFDocument.new()
	var creature_state := GLTFState.new()
	if creature_bytes.is_empty() or creature_gltf.append_from_buffer(
		creature_bytes, "", creature_state) != OK:
		push_error("Native smoke: cannot import authored hydro slime GLB")
		failed = true
	else:
		var creature_scene := creature_gltf.generate_scene(creature_state)
		if creature_scene == null:
			push_error("Native smoke: hydro slime GLB produced no scene")
			failed = true
		else:
			var material_names: Dictionary = {}
			var creature_nodes: Array[Node] = [creature_scene]
			while not creature_nodes.is_empty():
				var current: Node = creature_nodes.pop_back()
				if current is MeshInstance3D and current.mesh != null:
					var instance := current as MeshInstance3D
					for surface_idx in range(instance.mesh.get_surface_count()):
						var material := instance.mesh.surface_get_material(surface_idx)
						if material is BaseMaterial3D:
							material_names[material.resource_name] = true
				for child in current.get_children():
					creature_nodes.append(child)
			print("Native hydro slime imported materials: ", material_names.keys())
			if not material_names.has("SlimeFace"):
				push_error("Native smoke: hydro slime face material is missing")
				failed = true
			# Some GLBs carry extra authored names that do not appear on a
			# visible surface. These must not be treated as imported meshes.
			var glb_json_size: int = creature_bytes.decode_u32(12)
			if glb_json_size <= creature_bytes.size() - 20:
				var glb_document: Variant = JSON.parse_string(
					creature_bytes.slice(20, 20 + glb_json_size).get_string_from_utf8())
				if glb_document is Dictionary:
					var authored_materials: Array = (glb_document as Dictionary).get("materials", [])
					var authored_names: Array = []
					for entry in authored_materials:
						if entry is Dictionary:
							authored_names.append(entry.get("name", ""))
					print("Native hydro slime GLB authored materials: ", authored_names)
			creature_scene.free()

	# Closing chat/inventory after the OS loses focus must not recapture
	# mouse input. A detached player represents a windowless lifecycle and
	# checks that resume is focus-gated before touching the OS cursor.
	var player_script := ResourceLoader.load("res://src/Asteria.Godot/Gameplay/FpsPlayer.cs")
	if player_script == null:
		push_error("Native smoke: FpsPlayer C# script not loaded")
		failed = true
	else:
		var player: Node = player_script.new()
		if player == null:
			push_error("Native smoke: cannot construct FpsPlayer")
			failed = true
		else:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
			player.call("ResumeGameplay")
			if Input.mouse_mode != Input.MOUSE_MODE_VISIBLE:
				push_error("Native smoke: unfocused player recaptured the pointer")
				failed = true
			player.free()

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

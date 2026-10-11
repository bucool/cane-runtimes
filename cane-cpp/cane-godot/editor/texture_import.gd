@tool
extends RefCounted

# Cane's texture metadata describes the original pixels. Godot's general PNG
# importer may fix alpha borders, premultiply again, reduce or compress them.
# Import exact pixels into ordinary Texture2D resources instead. These resources
# are dependencies of the generated scene and remain available in exported PCKs.
static func preserve_pixels(data: CaneSkeletonData, generated: Array[String]) -> Error:
	var bundle: Dictionary = data.get_imported_bundle()
	var textures: Dictionary = bundle["textures"]
	var paths: Dictionary = bundle["texture_files"]
	for key in paths:
		var source: String = str(paths[key]).simplify_path()
		var bytes: PackedByteArray = FileAccess.get_file_as_bytes(source)
		var image := Image.new()
		var extension := source.get_extension().to_lower()
		var status: Error
		if extension == "png":
			status = image.load_png_from_buffer(bytes)
		elif extension == "jpg" or extension == "jpeg":
			status = image.load_jpg_from_buffer(bytes)
		elif extension == "webp":
			status = image.load_webp_from_buffer(bytes)
		else:
			status = image.load(source)
		if status != OK or image.is_empty():
			push_error("Cane: cannot import original texture pixels: " + source)
			return ERR_FILE_CORRUPT
		var texture := ImageTexture.create_from_image(image)
		var destination := "res://.godot/imported/cane-texture-" + source.md5_text() + ".res"
		status = ResourceSaver.save(texture, destination, ResourceSaver.FLAG_COMPRESS)
		if status != OK:
			return status
		textures[key] = ResourceLoader.load(destination, "Texture2D", ResourceLoader.CACHE_MODE_REPLACE)
		if not destination in generated:
			generated.append(destination)
	data.set_imported_bundle(bundle)
	return OK if data.get_last_error().is_empty() else ERR_INVALID_DATA

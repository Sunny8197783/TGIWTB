extends SceneTree

# Raw frame contact sheets, ordered south through south-west. No source assets are modified.
func _initialize():
	var dirs = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]
	for sex in ["male", "female"]:
		for clip in ["hero_stride", "hero_sprint", "hero_run", "hero_walk_fixed", "hero_slash_fixed", "hero_heavy_fixed"]:
			var sheet = Image.create_empty(9 * 160, 8 * 160, false, Image.FORMAT_RGBA8)
			sheet.fill(Color("25322d"))
			var found = false
			for row in range(8):
				for frame in range(9):
					var path = "res://art/heroes/%s/%s/%s/frame_%03d.png" % [sex, clip, dirs[row], frame]
					if not FileAccess.file_exists(path):
						continue
					var pose = Image.load_from_file(ProjectSettings.globalize_path(path))
					pose.convert(Image.FORMAT_RGBA8)
					pose.resize(160, 160, Image.INTERPOLATE_NEAREST)
					sheet.blend_rect(pose, Rect2i(0, 0, 160, 160), Vector2i(frame * 160, row * 160))
					found = true
			if found:
				sheet.save_png("res://docs/review/%s-%s-source.png" % [sex, clip])
	quit()

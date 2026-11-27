import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
blend, outdir, actname, nframes = argv[0], argv[1], argv[2], int(argv[3])
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
arm = bpy.data.objects["Horse"]
arm.animation_data.action = bpy.data.actions[actname]
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.light = 'STUDIO'
sc.render.resolution_x = 360; sc.render.resolution_y = 300
cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data); sc.collection.objects.link(cam)
cam.location = (6.0, 0.0, 1.2); cam.rotation_euler = (math.radians(88), 0, math.radians(90))
cam_data.type='ORTHO'; cam_data.ortho_scale = 3.4
sc.camera = cam
fr = arm.animation_data.action.frame_range
for i in range(nframes):
    f = int(fr[0] + (fr[1]-fr[0]) * i / nframes)
    sc.frame_set(f)
    sc.render.filepath = f"{outdir}/{actname}_{i:02d}.png"
    bpy.ops.render.render(write_still=True)

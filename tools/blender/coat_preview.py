import bpy, sys, math
argv = sys.argv[sys.argv.index("--")+1:]
bpy.ops.wm.open_mainfile(filepath=argv[0]); tex, out = argv[1], argv[2]
sc = bpy.context.scene
img = bpy.data.images.load(tex)
for m in bpy.data.materials:
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and 'basecolor' in (n.image.name if n.image else '').lower(): n.image = img
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'TEXTURE'; sc.display.shading.light='STUDIO'
sc.render.resolution_x=360; sc.render.resolution_y=260
cd=bpy.data.cameras.new("C"); c=bpy.data.objects.new("C",cd); sc.collection.objects.link(c)
c.location=(5.5,-3.5,1.6); c.rotation_euler=(math.radians(80),0,math.radians(57)); cd.type='ORTHO'; cd.ortho_scale=3.2; sc.camera=c
sc.render.filepath=out; bpy.ops.render.render(write_still=True)

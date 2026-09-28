using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>The existing map's tiles and footprints, rendered on a continuous height field.</summary>
public partial class ReferenceTerrain3D : Node3D
{
    public const float Unit = 32f;
    public const float WaterLevel = .14f;
    public static readonly Basis ArtBasis = new(Vector3.Right, -Mathf.Atan2(.66f,.75f));
    private static readonly Dictionary<Vector2, float> VertexHeights = new();
    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<(Texture2D Texture, bool Decal, bool Flip, int X, int Z), List<(Transform3D Transform, Color Color)>> _props = new();
    private readonly Dictionary<Texture2D, (Texture2D Texture, Vector2 Size, float CenterX)> _propTextures = new();
    public int TriangleCount { get; private set; }
    public int PropCount { get; private set; }

    public static bool CoastalWater(Vector2 point) => false;
    public static bool CoastalSand(Vector2 point) => false;

    private static float VertexHeight(float x, float z)
    {
        if(x<0 || z<0) return 1.2f;
        Vector2 key = new(x,z);
        if (VertexHeights.TryGetValue(key, out float cached)) return cached;
        float village = Mathf.SmoothStep(17f,33f, new Vector2(x-96,z-114).Length());
        float hills = 2.4f + 2.2f*Mathf.Sin(x*.042f)*Mathf.Cos(z*.045f);
        float edge=WorldLayout.ForestEdge(new Vector2(x,z));
        hills+=18f*(1f-Mathf.SmoothStep(0f,.09f,edge));
        float ridge = 3.5f*Mathf.SmoothStep(132f,160f,x)*(1f-Mathf.SmoothStep(70f,105f,z));
        float land = Mathf.Lerp(1.2f,hills+ridge,village);
        float bank = Mathf.SmoothStep(0f,6.5f,Mathf.Min(WorldLayout.RiverDistance(new(x,z)),WorldLayout.PoolDistance(new(x,z))));
        return VertexHeights[key] = Mathf.Max(WorldLayout.Bridge(new(x,z)) ? .30f : .12f, Mathf.Lerp(.12f,land,bank));
    }

    public static float HeightAt(Vector2 point)
    {
        Vector2 p = point / Unit;
        float step=1f/SurfaceDivisions(new(Mathf.FloorToInt(p.X),Mathf.FloorToInt(p.Y)));
        float x = Mathf.Floor(p.X/step)*step, z = Mathf.Floor(p.Y/step)*step, u = (p.X-x)/step, v = (p.Y-z)/step;
        float a = VertexHeight(x, z), b = VertexHeight(x + step, z);
        float c = VertexHeight(x, z + step), d = VertexHeight(x + step, z + step);
        return u + v <= 1f ? a + (b - a) * u + (c - a) * v
            : d + (c - d) * (1f - u) + (b - d) * (1f - v);
    }

    private static int SurfaceDivisions(Vector2I cell)
    {
        Vector2 p=(Vector2)cell+Vector2.One*.5f;
        return cell.X>=0 && cell.Y>=0 && (Mathf.Abs(WorldLayout.RiverDistance(p))<3f || Mathf.Abs(WorldLayout.PoolDistance(p))<3f) ? 4 : 1;
    }
    public static Vector3 Ground(Vector2 point) => new(point.X / Unit, HeightAt(point), point.Y / Unit);

    public void Build(TileWorld tiles)
    {
        var surfaces = new Dictionary<(int, int, int), SurfaceTool>();
        var walls = new Dictionary<(int, bool), List<int>>();
        var rockCells = new HashSet<Vector2I>();
        var water = WaterMaterial();
        foreach (Vector2I cell in tiles.GetUsedCells())
        {
            int sourceId = tiles.GetCellSourceId(cell);
            if (tiles.TileSet.GetSource(sourceId) is not TileSetAtlasSource atlas) continue;
            var coords = tiles.GetCellAtlasCoords(cell);
            Vector2 center = WorldLayout.TileCenter(cell.X, cell.Y);
            // The 2D Wang shoreline contains whole water tiles. In 3D it is ground beneath one curved ribbon.
            if (sourceId == 4 || (sourceId == 0 && coords.X is 4 or 5))
            { var grass=tiles.GrassTile; sourceId=grass.Source; atlas=(TileSetAtlasSource)tiles.TileSet.GetSource(sourceId); coords=grass.Atlas; }
            bool wet = CoastalWater(center);
            bool sand = CoastalSand(center);
            bool bridge = sourceId == 0 && coords.X == 6;
            int materialId = wet ? -1 : bridge ? -3 : sand ? -2 : sourceId;
            if (!_materials.ContainsKey(materialId))
            {
                using var groundImage = atlas.Texture.GetImage();
                groundImage.GenerateMipmaps();
                _materials[materialId] = wet ? water : bridge ? new ShaderMaterial
                {
                    Shader = new Shader { Code = """
                        shader_type spatial;
                        varying vec3 world;
                        void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
                        void fragment() {
                            float joint = step(0.075, fract(world.x * 4.0));
                            float grain = 0.92 + 0.08 * sin(world.z * 75.0 + sin(world.x * 17.0));
                            ALBEDO = mix(vec3(0.15, 0.09, 0.05), vec3(0.43, 0.28, 0.14) * grain, joint);
                            ROUGHNESS = 1.0;
                        }
                        """ }
                } : sand
                    ? new StandardMaterial3D { AlbedoColor = new Color(.78f, .7f, .5f), Roughness = 1f, VertexColorUseAsAlbedo = true }
                    : new ShaderMaterial
                {
                    Shader = new Shader { Code = """
                        shader_type spatial;
                        uniform sampler2D ground : source_color, filter_nearest_mipmap;
                        void fragment() {
                            vec3 detail = texture(ground, UV).rgb;
                            vec3 soft = textureLod(ground, UV, 3.0).rgb;
                            ALBEDO = mix(soft, detail, 0.65) * vec3(0.96, 0.96, 0.89);
                            ROUGHNESS = 1.0;
                        }
                        """ }
                };
                if (materialId >= 0 && _materials[materialId] is ShaderMaterial ground)
                    ground.SetShaderParameter("ground", ImageTexture.CreateFromImage(groundImage));
            }
            var key = (materialId, cell.X / 16, cell.Y / 16);
            if (!surfaces.TryGetValue(key, out var st))
            {
                st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
                st.SetMaterial(_materials[materialId]); surfaces[key] = st;
            }
            Rect2I region = atlas.GetTileTextureRegion(coords);
            Vector2 texSize = atlas.Texture.GetSize();
            Vector2 uv0 = (Vector2)region.Position / texSize;
            Vector2 uv1 = (Vector2)(region.Position + region.Size) / texSize;
            int divisions=SurfaceDivisions(cell);
            for(int z=0;z<divisions;z++) for(int x=0;x<divisions;x++)
            foreach (var corner in new[] { Vector2.Zero,Vector2.Right,Vector2.Down,Vector2.Right,Vector2.One,Vector2.Down })
            {
                Vector2 fraction=(new Vector2(x,z)+corner)/divisions, position=(Vector2)cell+fraction;
                float grain = sand ? .95f + .05f * Mathf.Sin(position.X * 27.1f + position.Y * 31.7f) : 1f;
                st.SetUV(uv0+(uv1-uv0)*fraction); st.SetColor(new Color(grain, grain, grain));
                st.AddVertex(new Vector3(position.X, wet ? WaterLevel : VertexHeight(position.X,position.Y),position.Y));
            }
            TriangleCount += 2*divisions*divisions;
            TileData tileData = tiles.GetCellTileData(cell);
            bool blocked = tileData != null && tileData.GetCollisionPolygonsCount(0) > 0
                && Mathf.Abs(cell.X + .5f - WorldLayout.RiverX(cell.Y + .5f)) > 5f
                && WorldLayout.PoolDistance(center/Unit)>2f;
            if (wet) AddBox(new Vector3(center.X / Unit, WaterLevel + 1f, center.Y / Unit), new Vector3(1f, 3f, 1f));
            else if (blocked)
            {
                if (sourceId == 0 && coords.X >= 15) { rockCells.Add(cell); continue; }
                var wallKey = (cell.Y, sourceId == 0 && coords.X >= 15);
                if (!walls.TryGetValue(wallKey, out var row)) walls[wallKey] = row = new List<int>();
                row.Add(cell.X);
            }
        }
        var wallMaterial = new StandardMaterial3D { AlbedoColor = new Color(.32f, .35f, .31f), Roughness = 1f };
        foreach (var (wallKey, row) in walls)
        {
            int z = wallKey.Item1;
            row.Sort();
            for (int i = 0; i < row.Count; i++)
            {
                int start = row[i], end = start;
                float low = VertexHeight(start, z), high = low;
                while (i + 1 < row.Count && row[i + 1] == end + 1)
                { end = row[++i]; float h = VertexHeight(end, z); low = Mathf.Min(low, h); high = Mathf.Max(high, h); }
                var size = new Vector3(end - start + 1f, high - low + 1.1f, 1f);
                var at = new Vector3((start + end + 1f) * .5f, low + size.Y * .5f, z + .5f);
                AddBox(at, size);
                AddChild(new MeshInstance3D { Position = at, Mesh = new BoxMesh { Size = size }, MaterialOverride = wallMaterial });
            }
        }
        BuildRocks(rockCells, wallMaterial);
        BuildRiver();
        foreach (var (key, st) in surfaces)
        {
            st.GenerateNormals(); st.Index();
            ArrayMesh mesh = st.Commit(); st.Dispose();
            AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            if (key.Item1 != -1)
            {
                var body = new StaticBody3D { CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() }); AddChild(body);
            }
        }
        if (tiles.Props != null)
            foreach (var node in tiles.Props.GetChildren())
            {
                if (node is Sprite2D sprite) AddProp(sprite);
            }
        BuildPropBatches();
        BuildUndergrowth(tiles);
        // The world ends in forested hills; no surrounding ocean plane.
        GD.Print($"[Reference3D] terrain triangles={TriangleCount} props={PropCount}");
    }

    private void BuildRocks(HashSet<Vector2I> cells, Material material)
    {
        while (cells.Count > 0)
        {
            Vector2I seed = default;
            foreach (var cell in cells) { seed = cell; break; }
            var queue = new Queue<Vector2I>(); queue.Enqueue(seed); cells.Remove(seed);
            Vector2I min = seed, max = seed;
            float low = float.MaxValue, high = float.MinValue;
            while (queue.Count > 0)
            {
                Vector2I cell = queue.Dequeue(); min = min.Min(cell); max = max.Max(cell);
                foreach (var corner in new[] { Vector2I.Zero, Vector2I.Right, Vector2I.Down, Vector2I.One })
                {
                    float h = VertexHeight(cell.X + corner.X, cell.Y + corner.Y);
                    low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                }
                foreach (var step in new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right })
                    if (cells.Remove(cell + step)) queue.Enqueue(cell + step);
            }
            Vector2I span = max - min + Vector2I.One;
            Vector3 size = new(span.X, high - low + 1.25f + (span.X + span.Y) * .06f, span.Y);
            using var sphere = new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 7, Rings = 3 };
            using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetSmoothGroup(uint.MaxValue);
            foreach (var vertex in sphere.GetFaces()) surface.AddVertex(vertex * size);
            surface.GenerateNormals(); surface.Index();
            var mesh = surface.Commit();
            Vector3 at = new((min.X + max.X + 1f) * .5f, low + size.Y * .5f - .1f, (min.Y + max.Y + 1f) * .5f);
            Material rockMaterial = material;
            var zone = WorldLayout.ZoneAt(new Vector2(at.X, at.Z) * Unit);
            if (zone != null)
            {
                int key = -10 - System.Array.IndexOf(WorldLayout.Zones, zone);
                if (!_materials.TryGetValue(key, out rockMaterial))
                    _materials[key] = rockMaterial = new StandardMaterial3D { Roughness = 1f,
                        AlbedoColor = zone.AccentColor.Lerp(new Color(.5f, .49f, .43f), .25f) };
            }
            AddChild(new MeshInstance3D { Position = at, Mesh = mesh, MaterialOverride = rockMaterial });
            var body = new StaticBody3D { Position = at, CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape() }); AddChild(body);
            body.AddToGroup("art_collision");
        }
    }

    private void BuildRiver()
    {
        using var water = new SurfaceTool(); water.Begin(Mesh.PrimitiveType.Triangles);
        water.SetMaterial(WaterMaterial());
        using var bank = new SurfaceTool(); bank.Begin(Mesh.PrimitiveType.Triangles);
        bank.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(.44f,.42f,.27f), Roughness = 1f });
        var body = new StaticBody3D { CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
        AddChild(body);
        for (float y = 0; y < WorldLayout.HeightTiles; y += .5f)
        {
            float next = y + .5f, x0 = WorldLayout.RiverX(y), x1 = WorldLayout.RiverX(next);
            if (!WorldLayout.IsForest(new(x0,y)) || !WorldLayout.IsForest(new(x1,next))) continue;
            float w0 = WorldLayout.RiverWidth(y), w1 = WorldLayout.RiverWidth(next);
            Vector2[] edge = { new(x0-w0,y), new(x0+w0,y), new(x1-w1,next), new(x1+w1,next) };
            foreach (int i in new[] { 0, 1, 2, 1, 3, 2 })
                water.AddVertex(new(edge[i].X,WaterLevel,edge[i].Y));
            if (WorldLayout.Bridge(new(x0,y+.25f))) continue;
            var points = new Vector3[8];
            for (int i=0;i<4;i++) { points[i]=new(edge[i].X,-.5f,edge[i].Y); points[i+4]=points[i]+Vector3.Up*3f; }
            body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } });
            foreach (int side in new[] { -1, 1 })
            {
                Vector2[] shore = { new(x0+side*w0,y),new(x0+side*(w0+.6f),y),
                    new(x1+side*w1,next),new(x1+side*(w1+.6f),next) };
                foreach (int i in side>0 ? new[] {0,1,2,1,3,2} : new[] {0,2,1,1,2,3})
                    bank.AddVertex(new(shore[i].X,Mathf.Max(WaterLevel,HeightAt(shore[i]*Unit))+.015f,shore[i].Y));
            }
        }
        foreach(var pool in WorldLayout.ForestPools)
        {
            var points=new Vector3[64];
            for(int i=0;i<32;i++)
            {
                Vector2 a=pool.Center+Vector2.Right.Rotated(i*Mathf.Tau/32f)*pool.Radius;
                Vector2 b=pool.Center+Vector2.Right.Rotated((i+1)*Mathf.Tau/32f)*pool.Radius;
                foreach(var p in new[]{pool.Center,b,a}) water.AddVertex(new(p.X,WaterLevel,p.Y));
                Vector2 outerA=pool.Center+(a-pool.Center)*1.15f,outerB=pool.Center+(b-pool.Center)*1.15f;
                foreach(var p in new[]{a,b,outerA,b,outerB,outerA})
                    bank.AddVertex(new(p.X,Mathf.Max(WaterLevel,HeightAt(p*Unit))+.015f,p.Y));
                points[i]=new(a.X,-.5f,a.Y);points[i+32]=points[i]+Vector3.Up*3f;
            }
            body.AddChild(new CollisionShape3D { Shape=new ConvexPolygonShape3D { Points=points } });
        }
        foreach (var surface in new[] { water, bank })
        { surface.GenerateNormals(); surface.Index(); AddChild(new MeshInstance3D { Mesh = surface.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }); }
    }

    private void BuildUndergrowth(TileWorld tiles)
    {
        using var surface=new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(new StandardMaterial3D { VertexColorUseAsAlbedo=true, Roughness=1f,
            CullMode=BaseMaterial3D.CullModeEnum.Disabled });
        foreach(var v in new[]{new Vector3(-.13f,0,0),new(0,.28f,0),new(.06f,0,0),
            new(0,0,-.09f),new(.03f,.2f,.02f),new(0,0,.12f)})
        { surface.SetColor(new(.43f,.61f,.25f)); surface.AddVertex(v); }
        surface.GenerateNormals(); var mesh=surface.Commit();
        var transforms=new List<Transform3D>();
        var rng=new RandomNumberGenerator { Seed=80926 };
        foreach(var cell in tiles.GetUsedCells())
        {
            if(cell.X<0 || cell.Y<0 || rng.Randf()>.36f) continue;
            Vector2 point=WorldLayout.TileCenter(cell.X,cell.Y);
            if(MeadowLayout.Reserved(point) || WorldLayout.Water(point/Unit)
                || Mathf.Abs(point.X/Unit-WorldLayout.RiverX(point.Y/Unit))<4f) continue;
            var local=cell-WorldLayout.Town.Tiles.Position;
            if(tiles.Town.IsRoadCell(local.X,local.Y)) continue;
            point+=new Vector2(rng.RandfRange(-12,12),rng.RandfRange(-12,12));
            var basis=new Basis(Vector3.Up,rng.Randf()*Mathf.Tau).Scaled(Vector3.One*rng.RandfRange(.6f,1.25f));
            transforms.Add(new(basis,Ground(point)+Vector3.Up*.025f));
        }
        var batch=new MultiMesh { TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=mesh,InstanceCount=transforms.Count };
        for(int i=0;i<transforms.Count;i++) batch.SetInstanceTransform(i,transforms[i]);
        AddChild(new MultiMeshInstance3D { Multimesh=batch,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private void AddBox(Vector3 at, Vector3 size)
    {
        var body = new StaticBody3D { Position = at, CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); AddChild(body);
    }

    private void AddProp(Sprite2D source)
    {
        // The bridge deck uses its actual walkable tiles; the old flat decoration has a different footprint.
        string kind = source.GetMeta("prop_kind", "").AsString();
        if (kind == "bridge") return;
        if (!_propTextures.TryGetValue(source.Texture, out var art))
        {
            using var image = source.Texture.GetImage();
            Rect2I used = image.GetUsedRect();
            if (used.Size.Y < 1) return;
            using var crop = image.GetRegion(used);
            art = (ImageTexture.CreateFromImage(crop), used.Size, used.Position.X + used.Size.X * .5f - image.GetWidth() * .5f);
            _propTextures[source.Texture] = art;
        }
        Vector2 foot = source.GlobalPosition + new Vector2(0f, 13f);
        bool decal = source.ZIndex < 0;
        bool flip = source.Scale.X < 0f;
        if (decal)
        {
            // Ground art shares the original bottom anchor and follows slopes, including the bridge.
            Vector2 corner = foot + new Vector2(art.CenterX * (flip ? -1f : 1f) - art.Size.X * .5f, -art.Size.Y);
            using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
            int columns = Mathf.CeilToInt(art.Size.X / 8f), rows = Mathf.CeilToInt(art.Size.Y / 8f);
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                foreach (var step in new[] { Vector2.Zero, Vector2.Right, Vector2.Down, Vector2.Right, Vector2.One, Vector2.Down })
                {
                    Vector2 uv = new((x + step.X) / columns, (y + step.Y) / rows);
                    surface.SetUV(new Vector2(flip ? 1f - uv.X : uv.X, uv.Y));
                    surface.AddVertex(Ground(corner + uv * art.Size) + Vector3.Up * .035f);
                }
            surface.GenerateNormals(); surface.Index();
            AddChild(new MeshInstance3D { Mesh = surface.Commit(),
                MaterialOverride = new StandardMaterial3D { AlbedoTexture = art.Texture, AlbedoColor = source.Modulate,
                    Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, Roughness = 1f,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            PropCount++; return;
        }
        var key = (source.Texture, decal, flip, Mathf.FloorToInt(foot.X / (Unit * 16f)), Mathf.FloorToInt(foot.Y / (Unit * 16f)));
        if (!_props.TryGetValue(key, out var batch)) _props[key] = batch = new();
        Vector3 origin = new(key.Item4 * 16f, 0f, key.Item5 * 16f);
        Basis basis = ArtBasis;
        Vector3 position = Ground(foot) + basis.Y * (art.Size.Y / Unit * .5f);
        position.X += art.CenterX * (flip ? -1f : 1f) / Unit;
        if (source.GetMeta("solid", false).AsBool())
        {
            var body = new StaticBody3D { Position = position - basis.Y * (art.Size.Y / Unit * .5f),
                CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
            AddChild(body);
            if (kind.Contains("tree"))
            {
                // Canopies sit above the walking plane; the visible trunk blocks feet.
                body.AddChild(new CollisionShape3D { Position = Vector3.Up*.4f,
                    Shape = new CylinderShape3D { Radius = .16f, Height = .8f } });
                body.AddToGroup("art_collision");
            }
            else { body.Basis = basis; SpriteCollision3D.Set(body, art.Texture, 1f / Unit, .24f, flip); }
        }
        Color tint = source.Modulate;
        if (kind.Contains("tree") && WorldLayout.Meadow.Tiles.HasPoint((Vector2I)(foot / Unit)))
        {
            int shade = Mathf.PosMod((int)(foot.X * 3 + foot.Y), 4);
            tint *= shade switch { 0 => new Color(.94f, 1f, .9f), 1 => new Color(1.12f, .98f, .7f),
                2 => new Color(.86f, .97f, .85f), _ => Colors.White };
        }
        batch.Add((new Transform3D(basis, position - origin), tint)); PropCount++;
    }

    private void BuildPropBatches()
    {
        foreach (var (key, instances) in _props)
        {
            var art = _propTextures[key.Texture];
            var material = new StandardMaterial3D { AlbedoTexture = art.Texture,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = .45f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
                BillboardKeepScale = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 1f };
            if (key.Flip) { material.Uv1Scale = new Vector3(-1f, 1f, 1f); material.Uv1Offset = new Vector3(1f, 0f, 0f); }
            var mesh = new QuadMesh { Size = art.Size / Unit, Material = material };
            var batch = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true, Mesh = mesh, InstanceCount = instances.Count };
            for (int i = 0; i < instances.Count; i++)
            { batch.SetInstanceTransform(i, instances[i].Transform); batch.SetInstanceColor(i, instances[i].Color); }
            AddChild(new MultiMeshInstance3D { Multimesh = batch, Position = new Vector3(key.X * 16f, 0f, key.Z * 16f),
                VisibilityRangeEnd = DevCapture.IsRequested() ? 0f : 70f,
                CastShadow = key.Decal ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.DoubleSided });
        }
        _props.Clear();
    }

    private static ShaderMaterial WaterMaterial() => new()
    {
        Shader = new Shader { Code = """
            shader_type spatial;
            render_mode cull_disabled;
            varying vec3 world;
            void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
            void fragment() {
                float waves = sin(world.x * 2.4 + sin(world.z * 2.0 + TIME) * 1.4 + TIME * 1.3)
                    * sin(world.z * 3.7 - TIME * 1.1 + sin(world.x * 1.5));
                float foam = smoothstep(0.70, 0.95, waves);
                ALBEDO = mix(vec3(0.08, 0.38, 0.49), vec3(0.72, 0.91, 0.94), foam * 0.35);
                ROUGHNESS = 0.24; SPECULAR = 0.65;
            }
            """ }
    };
}

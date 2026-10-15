"""Build the illustrated blender as an editable, volumetric OBJ asset (Y up)."""
import math
from pathlib import Path
from PIL import Image
import numpy as np
from scipy.spatial import Delaunay

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'UnityProject/Assets/Resources/Art/Juicer/Models'
vertices, uv, parts = [], [], []


def part(name, material):
    faces = []
    parts.append((name, material, faces))
    return faces


def vertex(p, tex=(0, 0)):
    vertices.append(p)
    uv.append(tex)
    return len(vertices)


def quad(faces, a, b, c, d):
    faces.extend([(a, b, c), (a, c, d)])


def lathe(name, material, profile, segments=32, depth=0.82):
    """Ordered bottom/outer/top/inner cross-section; reverse for inward surfaces."""
    faces = part(name, material)
    rings = []
    for j, (y, radius) in enumerate(profile):
        rings.append([vertex((radius*math.cos(i*2*math.pi/segments)*(1+.018*math.sin(i*6*math.pi/segments+.7)+.012*math.sin(i*14*math.pi/segments+y*2)), y,
                              radius*depth*math.sin(i*2*math.pi/segments)),
                             (i/segments, j/(len(profile)-1))) for i in range(segments)])
    for j in range(len(rings)-1):
        for i in range(segments):
            k = (i+1) % segments
            quad(faces, rings[j][i], rings[j+1][i], rings[j+1][k], rings[j][k])


def ring(name, material, y, radius, height, width=0.012, depth=0.82):
    lathe(name, material, [(y, radius-width), (y, radius), (y+height, radius),
                           (y+height, radius-width), (y, radius-width)], depth=depth)


def solid(name, material, profile, depth=0.82):
    lathe(name, material, [(profile[0][0], 0.001)] + profile + [(profile[-1][0], 0.001)], depth=depth)


def handle():
    # Broad illustrated grip, beveled in depth, with a small real opening.
    points = [(.20,.80),(.30,.83),(.45,.88),(.52,.86),(.56,.76),
              (.57,.62),(.52,.50),(.42,.44),(.27,.43),(.21,.47)]
    n=len(points)
    faces=part('Handle','Enamel')
    outer=[]; inner=[]
    for z,factor in [(-.058,.95),(-.038,1),(.038,1),(.058,.95)]:
        outer.append([vertex((.38+(x-.38)*factor,.65+(y-.65)*factor,z),((x-.18)/.42,(y-.42)/.48)) for x,y in points])
        inner.append([vertex((.30+.037*math.cos(math.pi/2-i*2*math.pi/n)/factor,
                              .728+.046*math.sin(math.pi/2-i*2*math.pi/n)/factor,z),(.5,.5)) for i in range(n)])
    # Triangulate the offset hole instead of joining rings with a crossing fan.
    front=outer[0]+inner[0]
    back=outer[-1]+inner[-1]
    coords=np.array([vertices[i-1][:2] for i in front])
    def inside(point, polygon):
        x,y=point; odd=False
        for i,(ax,ay) in enumerate(polygon):
            bx,by=polygon[i-1]
            if (ay>y)!=(by>y) and x < (bx-ax)*(y-ay)/(by-ay)+ax: odd=not odd
        return odd
    for tri in Delaunay(coords).simplices:
        center=coords[tri].mean(axis=0)
        if not inside(center,coords[:n]) or inside(center,coords[n:]): continue
        a,b,c=map(int,tri)
        cross=(coords[b,0]-coords[a,0])*(coords[c,1]-coords[a,1])-(coords[b,1]-coords[a,1])*(coords[c,0]-coords[a,0])
        if cross>0: b,c=c,b
        faces.append((front[a],front[b],front[c]))
        faces.append((back[a],back[c],back[b]))
    for i in range(n):
        j=(i+1)%n
        for k in range(3):
            quad(faces,outer[k][i],outer[k+1][i],outer[k+1][j],outer[k][j])
            quad(faces,inner[k][j],inner[k+1][j],inner[k+1][i],inner[k][i])


def box(name, material, center, size, angle=0):
    faces = part(name, material)
    ids = []
    for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]:
        px, pz = x*size[0]/2, z*size[2]/2
        ids.append(vertex((center[0]+px*math.cos(angle)-pz*math.sin(angle), center[1]+y*size[1]/2,
                           center[2]+px*math.sin(angle)+pz*math.cos(angle)), ((x+1)/2,(y+1)/2)))
    for corners in [(0,3,2,1),(4,5,6,7),(0,4,7,3),(1,2,6,5),(0,1,5,4),(3,7,6,2)]:
        quad(faces, *(ids[i] for i in corners))


def glass_edge(name, angle):
    faces = part(name, 'GlassEdge')
    rings=[]
    for y, r in [(.372,.155),(.79,.223),(.825,.229)]:
        cx,cz=r*math.cos(angle),r*.82*math.sin(angle)
        rings.append([vertex((cx+.003*math.cos(k*math.pi/6),y,cz+.003*math.sin(k*math.pi/6))) for k in range(12)])
    for j in range(len(rings)-1):
        for k in range(12):
            quad(faces,rings[j][k],rings[j+1][k],rings[j+1][(k+1)%12],rings[j][(k+1)%12])


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    # Reference silhouette: tapered dark pedestal, glass jug, broad side grip, stepped lid.
    solid('RubberFoot', 'Rubber', [(.006,.214),(.014,.235),(.032,.235),(.04,.222)])
    solid('MotorBase', 'Enamel', [(.032,.24),(.05,.28),(.09,.285),(.24,.252),(.29,.212),(.32,.17)])
    ring('BaseLowerSeam','Ink',.043,.245,.009,.010)
    ring('JugSeat','Ink',.313,.17,.021,.016)
    solid('JugBottom','Enamel',[(.332,.145),(.346,.15),(.361,.152)])
    # Glass is a closed thin shell with explicit inner wall and rim, not a solid cylinder.
    lathe('GlassJug','Glass',[(.350,.149),(.370,.155),(.79,.223),(.827,.23),
                             (.839,.224),(.839,.213),(.79,.211),(.373,.142),(.350,.137),(.350,.149)])
    ring('JugTopRim','Ink',.826,.234,.020,.017)
    ring('JugBottomRim','Ink',.352,.158,.016,.012)
    glass_edge('JugEdgeLeft',math.pi*1.15)
    glass_edge('JugEdgeRight',math.pi*1.85)
    handle()
    solid('Lid','Enamel',[(.844,.225),(.855,.244),(.873,.242),(.891,.204),(.909,.196)])
    ring('LidSeam','Ink',.851,.244,.009,.014)
    solid('LidCap','Enamel',[(.904,.126),(.92,.134),(1.034,.131),(1.049,.12)])
    ring('CapTopEdge','Ink',1.032,.132,.013,.012)
    for i in range(3):
        # Subtle slits echo the dark marks on the illustrated cap.
        box('CapGroove'+str(i),'Ink',(.025, .944+i*.027, -.107),(.09,.012,.009))
    solid('Juice','Juice',[(.365,.137),(.377,.14),(.487,.157),(.493,.156)],depth=.82)
    solid('JuiceSurface','JuiceTop',[(.491,.15),(.492,.157),(.495,.156)],depth=.82)
    solid('BladeHub','Steel',[(.365,.022),(.404,.022),(.418,.011)],depth=1)
    for i in range(4):
        angle=i*math.pi/2
        box('Blade'+str(i),'Steel',(.057*math.cos(angle),.39,.057*math.sin(angle)),(.11,.009,.024),angle)
    # Raised black calibration marks follow the taper on the front of the jug.
    for i in range(5):
        y=.43+i*.065
        r=.155+(y-.37)/.42*.068
        x=-r*.59
        z=-math.sqrt(max(0,r*r-x*x))*.82-.003
        box('Measure'+str(i),'Ink',(x,y,z),(.035 if i%2==0 else .023,.008,.008))
    for i in range(7):
        box('BaseVent'+str(i),'Ink',((i-3)*.027,.109,.201),(.011,.052,.009))
    # Paper grain, uneven ink wash and dry-brush patches echo the source art.
    rng=np.random.default_rng(26)
    size=512
    yy,xx=np.mgrid[0:size,0:size]/size
    wash=np.zeros((size,size))
    for _ in range(22):
        cx,cy=rng.random(2); rx,ry=rng.uniform(.035,.19,2)
        shape=((xx-cx)/rx)**2+((yy-cy)/ry)**2
        wash+=np.clip(1-shape,0,1)*rng.uniform(-18,22)
    grain=rng.normal(0,9,(size,size))
    strokes=np.sin(xx*53+np.sin(yy*17)*1.4)*2
    base=np.array([61,76,87])[None,None,:]
    paint=np.clip(base+(wash+grain+strokes)[...,None],0,255).astype('uint8')
    Image.fromarray(paint).save(OUT/'enamel_brush.png')
    paper=np.clip(np.array([204,177,144])[None,None,:]+(wash*.4+grain*.6)[...,None],0,255).astype('uint8')
    Image.fromarray(paper).save(OUT/'glass_paper.png')
    materials={'Enamel':((.64,.68,.70),1),'Ink':((.038,.046,.049),1),
               'Rubber':((.055,.06,.058),1),'Glass':((.80,.69,.56),.30),
               'Juice':((.56,.035,.025),1),'JuiceTop':((.75,.065,.035),1),
               'Steel':((.57,.57,.49),1),'GlassEdge':((.43,.56,.58),1)}
    mtl=[]
    for name,(color,alpha) in materials.items():
        mtl += [f'newmtl {name}', 'Kd '+' '.join(map(str,color)),f'd {alpha}', 'Ns 50', 'illum 2']
        if name=='Enamel': mtl += ['map_Kd enamel_brush.png']
        if name=='Glass': mtl += ['map_Kd glass_paper.png']
    (OUT/'blender_reference.mtl').write_text('\n'.join(mtl)+'\n',encoding='utf-8')
    lines=['# Editable reference-built blender. Units: meters before scene fitting. Y up.',
           'mtllib blender_reference.mtl']
    lines += ['v %.7f %.7f %.7f'%v for v in vertices]
    lines += ['vt %.6f %.6f'%t for t in uv]
    for name,material,faces in parts:
        lines += ['o '+name,'g '+name,'usemtl '+material,'s off' if name=='Handle' else 's 1']
        lines += ['f '+' '.join(f'{i}/{i}' for i in face) for face in faces]
    (OUT/'blender_reference.obj').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(f'{len(parts)} parts; {len(vertices)} vertices; {sum(len(x[2]) for x in parts)} triangles -> {OUT}')


if __name__=='__main__':
    build()

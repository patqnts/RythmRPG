"""Cadence Thorn v2: reference-traced glyphs and a matching constructed alphabet.
Use --deps <folder> for fonttools, skia-pathops and opencv-python-headless.
Full title silhouettes are available at U+E000 and U+E001, independent of ligature support.
"""
from pathlib import Path
import sys,json,math,importlib.util
if '--deps' in sys.argv:sys.path.insert(0,sys.argv[sys.argv.index('--deps')+1])
import cv2,numpy as np,pathops
from PIL import Image
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.pens.svgPathPen import SVGPathPen

ROOT=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('original',ROOT/'Source~'/'build_font.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
p,union,cut,move=base.p,base.union,base.cut,base.move
masks=[(np.array(Image.open(ROOT/'Source~'/'References'/f))[:,:,3]>128).astype('uint8')
       for f in ['RhythmRPG.png','AGodMustDie.png']]
components=[]
for mask in masks:
    n,labels,stats,centers=cv2.connectedComponentsWithStats(mask,8)
    components.append((labels,stats))

def selection(image,boxes=None,poly=None):
    mask=masks[image].copy()
    if boxes:
        labels,stats=components[image]
        ids=[]
        for bx,by in boxes:
            index=min(range(1,len(stats)),key=lambda k:(int(stats[k,0])-bx)**2+(int(stats[k,1])-by)**2)
            ids.append(index)
        mask=np.isin(labels,ids).astype('uint8')
    if poly:
        keep=np.zeros_like(mask);cv2.fillPoly(keep,[np.array(poly,dtype='int32')],1)
        mask &= keep
    return mask

def vector(mask):
    contours,hierarchy=cv2.findContours(mask,cv2.RETR_TREE,cv2.CHAIN_APPROX_SIMPLE)
    result=pathops.Path()
    if hierarchy is None:return result
    # Accumulate each ring with the correct winding before simplifying once.
    for i,contour in enumerate(contours):
        if abs(cv2.contourArea(contour))<6:continue
        points=cv2.approxPolyDP(contour,.45,True)[:,0,:]
        if len(points)<3:continue
        ring=pathops.Path();ring.moveTo(*map(float,points[0]))
        for xy in points[1:]:ring.lineTo(*map(float,xy))
        ring.close()
        depth=0;j=hierarchy[0,i,3]
        while j>=0:depth+=1;j=hierarchy[0,j,3]
        result=pathops.op(result,ring,pathops.PathOp.DIFFERENCE if depth%2 else pathops.PathOp.UNION,clockwise=True)
    return result

def bounds(shape):
    pen=BoundsPen(None);shape.draw(pen);return pen.bounds

def traced(image,height,boxes=None,poly=None,bottom=0):
    shape=vector(selection(image,boxes,poly));x0,y0,x1,y1=bounds(shape)
    s=height/(y1-y0)
    return shape.transform(s,0,0,-s,-x0*s,y1*s+bottom)

def fit(shape,height,width=None,bottom=0):
    x0,y0,x1,y1=bounds(shape);s=height/(y1-y0)
    sx=s if width is None else width/(x1-x0)
    return shape.transform(sx,0,0,s,-x0*s,bottom-y0*s)

G={};provenance={}
def add(char,image,height,boxes=None,poly=None,bottom=0):
    G[char]=traced(image,height,boxes,poly,bottom)
    provenance[char]={'reference':image,'height':height,'components':boxes,'cutPolygon':poly,'bottom':bottom}

add('A',1,990,[(113,224)])
add('G',0,840,[(794,661)])
add('P',0,835,[(570,659)])
add('D',1,900,[(747,688)])
add('C',1,795,[(462,393)])
add('o',1,510,[(759,461)])
add('d',1,805,[(891,328)])
add('e',1,510,[(1092,736)])
add('i',1,665,[(1008,698),(980,769)])
add('t',0,865,[(654,271)])
add('R',0,915,[(46,312)],[(0,260),(453,260),(455,421),(407,462),(355,496),(400,571),(440,622),(486,642),(385,693),(0,790)])
add('h',0,740,[(46,312)],[(516,348),(585,348),(585,669),(454,669),(440,625),(425,585),(416,515),(407,468)])
add('m',0,540,[(775,374)],[(966,474),(1260,426),(1260,711),(931,711),(933,628),(943,554)])
add('y',0,790,[(205,432)],[(533,457),(714,420),(714,542),(671,633),(603,708),(541,753),(471,783),(390,787),(506,730),(562,657),(583,600)],bottom=-250)
add('M',1,995,[(34,638)],[(20,646),(401,646),(384,703),(353,755),(353,827),(384,882),(319,919),(283,946),(267,989),(20,1070)])
add('u',1,535,[(34,638)],[(368,750),(434,700),(434,762),(497,711),(497,830),(548,929),(438,937),(371,901),(352,857),(353,785)])
add('s',1,555,[(34,638),(519,812)],[(458,681),(734,652),(737,989),(474,989),(474,947),(527,903),(509,851),(533,802),(509,755),(474,727)])
G['O']=fit(G['o'],820)
# Reconstruct contact edges where the source logo joins neighbouring glyphs.
# R's curved outline is redrawn in the original image coordinates, then flipped.
rOuter=p('M56 397 Q180 358 325 318 C425 285 485 389 407 452 Q369 483 294 504 '
         'Q347 503 389 576 Q426 640 488 641 Q382 681 334 614 L279 548 '
         'Q259 520 230 520 L212 617 Q207 670 330 651 Q171 695 44 752 '
         'Q122 699 144 599 Q172 545 109 545 Q170 532 180 457 L124 487 '
         'Q170 436 175 416 Q165 379 56 397 Z')
rCounter=p('M255 353 L232 503 C331 480 391 386 338 350 Q316 341 255 353 Z')
G['R']=fit(cut(rOuter,rCounter).transform(1,0,0,-1,0,0),1000,bottom=-160)
G['h']=p('M-80 -72 Q13 12 25 133 L41 526 Q0 546 -70 520 L209 829 '
         'Q122 615 111 412 Q314 659 417 462 Q466 382 424 176 Q410 56 521 -42 '
         'Q387 -6 289 -28 Q349 22 345 134 L354 363 Q356 446 310 449 '
         'Q244 420 161 329 L142 155 Q133 69 190 22 Q64 27 -80 -72 Z')
G['m']=p('M-100 -70 Q0 40 28 165 L57 400 Q50 431 -30 443 L219 601 L190 443 '
         'Q334 628 421 497 Q511 622 585 532 L640 574 L714 516 Q660 485 668 411 '
         'L676 112 Q685 14 808 -70 Q684 -24 559 -52 Q580 10 565 115 L560 396 '
         'Q559 464 511 454 Q468 430 437 391 L436 130 Q433 42 510 -11 L314 -29 '
         'Q336 23 329 112 L335 396 Q336 464 284 459 Q234 428 178 337 L146 137 '
         'Q132 69 208 16 Q70 18 -100 -70 Z')
G['m']=fit(G['m'],585,bottom=-45)
G['u']=p('M-86 500 L126 632 L106 157 Q105 51 176 48 Q251 69 281 138 '
         'L294 438 Q293 486 234 473 L489 605 Q421 469 416 374 L403 110 '
         'Q400 8 498 -56 Q391 -13 268 -102 L290 19 Q129 -130 11 -8 '
         'Q-20 18 -11 132 L2 392 Q8 446 -86 500 Z')
G['u']=fit(G['u'],595,bottom=-60)
G['M']=p('M-80 -185 Q115 -6 109 255 L116 644 Q116 740 -99 684 L222 852 '
         'L427 666 L776 909 Q681 755 665 577 L663 215 Q662 89 866 143 '
         'Q637 -14 464 91 L487 238 L496 599 L313 307 L246 557 L233 268 '
         'Q215 63 417 91 Q107 13 -80 -185 Z')
G['M']=fit(G['M'],1030,bottom=-160)
G['S']=union(fit(base.G['S'],835,730),p('M95 65 Q251 -64 687 -51 Q359 -225 -95 -50 Q72 -36 95 65 Z'))
G['S']=fit(G['S'],870,bottom=-65)
G['s']=fit(G['S'],580,bottom=-40)
G['y']=p('M-51 532 L204 590 Q114 528 167 403 L249 199 L362 439 '
         'Q403 534 353 535 L510 646 Q416 471 340 298 Q143 -152 -156 -254 '
         'Q75 -99 182 123 L62 398 Q21 497 -51 532 Z')
G['y']=fit(G['y'],790,bottom=-250)
G['D']=move(G['D'],y=-130)
G['G']=move(G['G'],y=-50)
for char in ['R','h','m','u','M','s','y']:
    provenance[char]={'reference':0 if char in ['R','h','m','y'] else 1,'method':'contact edges redrawn from reference silhouette'}

def stem(x,top=820,bottom=0,w=188):
    # Deep, curved wedges taken from the reference's downstrokes.
    return p(f'M{x-130} {bottom-105} Q{x+20} {bottom+30} {x+28} {bottom+150} '
             f'L{x+45} {top-140} Q{x+55} {top-63} {x-118} {top-82} '
             f'Q{x+64} {top-17} {x+w+100} {top+45} '
             f'Q{x+w-7} {top-95} {x+w-13} {top-200} '
             f'L{x+w-26} {bottom+166} Q{x+w-38} {bottom+75} {x+w+130} {bottom+24} '
             f'Q{x+42} {bottom-12} {x-130} {bottom-105} Z')
def blade(x,y,w,h=70):
    return p(f'M{x-65} {y-34} Q{x+w*.35} {y+h+22} {x+w+100} {y+h+66} '
             f'Q{x+w-35} {y+30} {x+w-52} {y-45} Q{x+w*.55} {y+54} {x-65} {y-34} Z')

# Missing characters use traced bowls and the same broad, curved terminal vocabulary.
G['B']=union(stem(118),move(fit(G['D'],478,538),x=91,y=360),move(fit(G['D'],496,628),x=88,y=-15))
G['E']=union(stem(115),blade(235,739,420,82),blade(219,370,330,45),blade(206,-2,459,67))
G['F']=union(stem(115),blade(235,739,440,82),blade(219,380,350,45))
G['H']=union(stem(122),stem(676),blade(253,346,530,48))
G['I']=stem(137,820,0,222)
G['J']=union(stem(392,820,173,214),p('M406 342 L577 329 C607 -119 158 -169 8 12 '
                  'L175 213 Q164 -2 311 44 Q406 89 406 342 Z'))
G['K']=union(stem(134),p('M265 305 L642 737 Q705 819 643 848 L930 892 '
               'Q777 793 725 730 L483 442 Q603 451 658 297 Q739 101 952 -64 '
               'Q703 -83 599 44 L394 332 Q329 397 265 305 Z'))
G['L']=union(stem(139),p('M243 25 Q529 72 776 176 L696 -52 Q462 -3 144 -20 Z'))
G['N']=union(stem(94,820,0,108),stem(731,820,0,105),
            p('M150 820 L328 853 L791 162 L771 -76 L705 -38 L173 723 Z'))
G['Q']=union(G['O'],p('M314 190 Q486 203 583 11 Q707 -162 913 -74 Q742 -241 565 -123 Q463 -30 314 190 Z'))
G['T']=union(stem(304),blade(20,738,849,79))
G['U']=fit(G['u'],820)
G['V']=p('M-77 820 L296 880 Q207 764 256 635 L428 212 L650 685 Q697 800 616 825 '
         'L897 909 Q760 800 698 650 L341 -88 L112 578 Q67 725 -77 820 Z')
G['W']=union(move(G['V'],sx=.8),move(G['V'],x=443,sx=.8))
G['X']=union(p('M-40 820 L304 884 Q223 791 293 687 L695 133 Q774 23 924 -60 '
               'Q697 -9 583 -64 Q636 15 554 124 L158 674 Q77 782 -40 820 Z'),
             p('M630 849 L900 901 Q755 777 665 636 L207 109 Q155 42 234 -20 L-52 -92 '
               'Q106 24 176 128 L590 630 Q707 796 630 849 Z'))
G['Y']=union(move(G['V'],y=350,sx=.8,sy=.55),stem(223,403,0,190))
G['Z']=p('M22 760 L832 883 L792 805 L237 75 Q573 112 842 221 L731 -68 '
         'Q411 -8 -63 -93 L10 47 L588 764 Q264 723 -23 560 Z')
G['a']=union(G['o'],move(fit(stem(86,505,0,140),535,269),x=350))
G['b']=union(fit(stem(102,751,0,181),795,340),move(G['o'],x=171))
G['c']=fit(G['C'],510,529)
G['f']=union(fit(G['t'],868,442,bottom=-200),p('M192 571 Q195 791 448 763 L526 696 '
                          'Q342 742 341 551 Z'))
G['g']=union(G['o'],p('M363 535 L549 611 Q481 481 482 277 L482 2 C487 -187 244 -314 13 -273 '
                          'Q319 -192 333 -27 Z'))
G['j']=union(G['i'],p('M137 102 L288 134 Q301 -188 22 -282 Q143 -149 137 102 Z'))
G['k']=union(fit(stem(110,735,0,169),795,345),move(fit(G['K'],520,495),x=145))
G['l']=fit(stem(95,750,0,162),795,377)
G['n']=cut(G['h'],p('M-100 548 L1500 548 L1500 1700 L-100 1700 Z'))
G['n']=union(G['n'],p('M-17 420 L199 568 Q157 478 163 411 Z'))
G['p']=union(move(G['o'],x=166),fit(stem(91,505,-245,171),806,342,bottom=-250))
G['q']=union(G['o'],move(fit(stem(91,505,-245,171),806,342,bottom=-250),x=328))
G['r']=union(fit(stem(91,505,0,164),550,361),p('M217 341 Q391 667 577 508 L436 356 '
                         'Q386 469 318 387 L247 277 Z'))
G['v']=fit(G['V'],540)
G['w']=fit(G['W'],540)
G['x']=fit(G['X'],540)
G['z']=fit(G['Z'],540)

# Numerals and punctuation retain familiar skeletons with broader calligraphic weight.
for char,shape in base.G.items():
    if char in G:continue
    if char.isdigit():
        shape=union(*(move(shape,x=i*5) for i in range(-9,10)))
        G[char]=fit(shape,790)
    else:G[char]=move(shape,sx=1.16,sy=1.16)

# Exact two-line title outlines. These remain text glyphs, not bitmap sprites.
G['\ue000']=traced(0,1100)
G['\ue001']=traced(1,1100)

def build():
    names=['.notdef'];glyphs={};metrics={};cmap={}
    notdef=cut(p('M0 0 L440 0 L440 800 L0 800 Z'),p('M60 60 L380 60 L380 740 L60 740 Z'))
    for char,outline in [(None,notdef)]+sorted(G.items(),key=lambda v:ord(v[0])):
        name='.notdef' if char is None else f'uni{ord(char):04X}'
        if char is not None:names.append(name);cmap[ord(char)]=name
        bb=bounds(outline)
        if bb:
            x0,y0,x1,y1=bb
            outline=move(outline,x=18-x0)
            advance=round(x1-x0+36);lsb=18
        else:advance=265;lsb=0
        pen=TTGlyphPen(None);outline.draw(Cu2QuPen(pen,max_err=.55));glyphs[name]=pen.glyph();metrics[name]=(advance,lsb)
    fb=FontBuilder(1000,isTTF=True)
    fb.setupGlyphOrder(names);fb.setupCharacterMap(cmap);fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics);fb.setupHorizontalHeader(ascent=1180,descent=-350,lineGap=10)
    fb.setupNameTable({'familyName':'Cadence Thorn Reference','styleName':'Regular','fullName':'Cadence Thorn Reference',
        'psName':'CadenceThorn-Reference','uniqueFontIdentifier':'Cadence Thorn Reference 2.000',
        'version':'Version 2.000','description':'Reference-traced fantasy lettering with broad curved wedges and swashes.'})
    fb.setupOS2(sTypoAscender=1180,sTypoDescender=-350,sTypoLineGap=10,usWinAscent=1180,usWinDescent=350,
        sxHeight=535,sCapHeight=835,usWeightClass=700,usWidthClass=5,fsSelection=0x40,fsType=0)
    fb.setupPost();fb.setupMaxp()
    features='languagesystem DFLT dflt; languagesystem latn dflt; feature kern {\n'
    pairs={'Rh':-48,'hy':-25,'yt':-65,'th':-18,'hm':-45,'AV':-100,'AW':-90,'AY':-80,'VA':-95,
           'WA':-90,'YA':-90,'To':-50,'Te':-45,'Ty':-40,'Go':-48,'Mu':-60,'st':-30,'Di':-25,'ie':-10}
    for pair,k in pairs.items():features+=f'pos {cmap[ord(pair[0])]} {cmap[ord(pair[1])]} {k};\n'
    features+='} kern;'
    addOpenTypeFeaturesFromString(fb.font,features)
    destination=Path(sys.argv[sys.argv.index('--output')+1]) if '--output' in sys.argv else ROOT/'CadenceThorn-Reference.ttf'
    fb.save(destination)
    test=TTFont(destination,checkChecksums=2);test.ensureDecompiled()
    assert all(i in test.getBestCmap() for i in range(32,127))
    (ROOT/'Source~'/'reference-glyphs.json').write_text(json.dumps(provenance,indent=2))
    report={'version':2,'characters':len(cmap),'directlyTracedLetters':list(provenance),'titleGlyphs':['U+E000','U+E001'],
            'asciiComplete':True,'bytes':destination.stat().st_size}
    (ROOT/'Source~'/'build-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
if __name__=='__main__':build()

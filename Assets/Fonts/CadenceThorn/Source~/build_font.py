"""Build Cadence Thorn: original, editable vector outlines for a fantasy display face.

Requires fonttools and skia-pathops. No outlines from another font are used.
Usage: python build_font.py [--deps path-to-python-packages]
"""
from pathlib import Path
import sys, math, json
if '--deps' in sys.argv:
    sys.path.insert(0, sys.argv[sys.argv.index('--deps') + 1])
import pathops
from fontTools.svgLib.path import parse_path
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.fontBuilder import FontBuilder
from fontTools.ttLib import TTFont
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString

ROOT = Path(__file__).resolve().parent.parent
G = {}

def p(svg):
    result = pathops.Path()
    parse_path(svg, result.getPen())
    return pathops.simplify(result, clockwise=True)

def union(*shapes):
    result = pathops.Path()
    for shape in shapes:
        result = pathops.op(result, shape, pathops.PathOp.UNION, clockwise=True)
    return result

def cut(shape, *holes):
    for hole in holes:
        shape = pathops.op(shape, hole, pathops.PathOp.DIFFERENCE, clockwise=True)
    return shape

def move(shape, x=0, y=0, sx=1, sy=1):
    return shape.transform(sx, 0, 0, sy, x, y)

def stem(x, top=700, bottom=0, width=126):
    # Concave flares end in sweeping wedges instead of slab serifs.
    return p(f'M{x-58} {bottom-24} Q{x+12} {bottom+14} {x+14} {bottom+85} '
             f'L{x+14} {top-82} Q{x+14} {top-26} {x-50} {top-8} '
             f'L{x+width+66} {top+43} Q{x+width} {top-10} {x+width} {top-64} '
             f'L{x+width} {bottom+85} Q{x+width} {bottom+18} {x+width+75} {bottom-20} '
             f'Q{x+58} {bottom+10} {x-58} {bottom-24} Z')

def bar(x,y,w,h=38):
    return p(f'M{x-30} {y-12} Q{x+w*.35} {y+h+12} {x+w+36} {y+h+34} '
             f'L{x+w-2} {y-7} Q{x+w*.45} {y+h-10} {x-30} {y-12} Z')

def downstroke(x,top=391,width=99):
    # Smooth shoulder join on h/n/m; no extra serif at the arch junction.
    return p(f'M{x} {top} L{x+width} {top} L{x+width} 85 '
             f'Q{x+width} 18 {x+width+75} -20 Q{x+58} 10 {x-58} -24 '
             f'Q{x+12} 14 {x+14} 85 Z')

def diamond(x,y,r=34):
    return p(f'M{x-r} {y} Q{x-6} {y+10} {x} {y+r} Q{x+10} {y+8} {x+r} {y} '
             f'Q{x+8} {y-10} {x} {y-r} Q{x-8} {y-8} {x-r} {y} Z')

def ring(w=550,h=700):
    # The almond counter and pointed poles echo the supplied lettering.
    outer=p(f'M{w*.53} {h+12} C{w*.88} {h*.93} {w+15} {h*.73} {w} {h*.44} '
            f'C{w-12} {h*.19} {w*.8} {h*.05} {w*.47} -18 '
            f'C{w*.12} {h*.05} -15 {h*.27} 0 {h*.55} C12 {h*.8} {w*.2} {h*.96} {w*.53} {h+12} Z')
    inner=p(f'M{w*.43} {h-62} C{w*.21} {h*.73} {w*.19} {h*.31} {w*.56} 48 '
            f'C{w*.8} {h*.29} {w*.82} {h*.7} {w*.43} {h-62} Z')
    return cut(outer,inner)

G['A']=union(p('M20 -16 Q108 29 141 106 L431 784 Q403 658 465 564 L634 82 Q665 16 737 -25 '
              'Q617 5 483 -20 Q545 19 516 93 L343 557 L174 97 Q149 36 226 -10 Z'),
             p('M-68 188 Q172 355 514 309 L552 258 Q263 326 -68 188 Z'))
G['B']=union(stem(78),cut(p('M173 690 C501 741 611 584 505 434 Q465 391 397 366 '
                          'C660 329 663 72 387 4 L180 8 Z'),
                       p('M204 638 L204 406 C433 416 451 623 204 638 Z'),
                       p('M204 353 L204 57 C466 69 500 323 204 353 Z')))
G['C']=p('M594 719 L530 520 Q500 675 355 654 C115 619 91 119 332 53 Q443 20 569 145 '
         'Q520 -5 355 -20 C-95 -19 -101 612 306 699 Q458 734 594 719 Z')
G['D']=union(stem(75),cut(p('M185 701 C686 686 739 94 218 -10 L173 31 Z'),
                       p('M201 634 L201 54 C510 101 556 519 201 634 Z')))
G['E']=union(stem(78),p('M161 640 L554 712 L507 541 Q451 662 184 589 Z'),
             p('M169 36 Q391 120 575 146 L511 -8 L158 -14 Z'),bar(178,331,270))
G['F']=union(stem(78),p('M160 640 L564 710 L505 540 Q451 657 184 590 Z'),bar(178,335,273))
G['G']=union(G['C'],p('M292 344 Q458 412 672 425 Q573 377 567 310 L567 90 Q553 -61 447 -125 '
                      'Q471 -22 446 90 L446 278 Q447 337 292 344 Z'))
G['H']=union(stem(75),stem(454),bar(194,329,301))
G['I']=stem(76,700,0,139)
G['J']=union(stem(295,700,200),p('M309 267 L421 267 L421 156 C411 -75 131 -67 35 14 '
                             'L107 176 Q114 46 207 42 Q300 34 309 190 Z'))
G['K']=union(stem(75),p('M190 317 L469 606 Q510 654 450 686 L659 722 '
                      'Q548 661 469 570 L291 372 Q363 375 397 305 L509 120 '
                      'Q579 16 711 -44 Q511 -28 430 26 L267 284 Q232 323 190 283 Z'))
G['L']=union(stem(78),p('M166 37 Q379 100 568 139 L514 -15 Q314 20 156 -11 Z'))
G['M']=union(stem(60,700,0,78),stem(584,700,0,124),
             p('M103 697 L224 722 L401 303 L598 704 L658 670 L376 17 L176 536 L140 4 L102 4 Z'))
G['N']=union(stem(67,700,0,67),stem(522,700,0,68),p('M98 701 L220 715 L570 170 L570 -31 L504 38 L111 630 Z'))
G['O']=ring(612,700)
G['P']=union(stem(75),cut(p('M177 689 C674 782 727 307 182 283 Z'),
                       p('M202 634 L202 343 C490 323 538 648 202 634 Z')))
G['Q']=union(G['O'],p('M288 140 Q409 175 490 60 Q550 -34 714 -56 Q533 -151 424 -26 Q352 77 288 140 Z'))
G['R']=union(G['P'],p('M196 352 Q328 379 411 222 Q485 71 582 18 Q649 -22 774 -9 '
                     'Q593 -118 441 -20 Q363 31 302 163 Q249 283 196 352 Z'))
G['S']=p('M574 711 L513 521 Q472 681 303 636 C186 605 179 511 294 456 L426 392 '
         'C704 259 561 -27 279 -16 Q134 -9 35 -43 L93 179 Q138 14 299 55 '
         'C448 89 448 191 312 254 L191 313 C-83 455 104 770 381 704 Q494 682 574 711 Z')
G['T']=union(stem(264),p('M29 680 Q333 670 686 730 L625 553 Q592 659 391 623 L168 623 Q62 611 0 541 Z'))
G['U']=union(stem(54,700,262),stem(496,700,0,68),
             p('M68 328 L180 328 L180 225 C180 -13 499 4 510 214 L548 241 '
               'C547 -106 66 -107 68 215 Z'))
G['V']=p('M-20 704 L218 722 Q160 670 187 599 L347 169 L495 605 Q520 674 458 692 '
         'L639 723 Q556 658 530 592 L307 -30 L103 531 Q58 653 -20 704 Z')
G['W']=union(move(G['V'],sx=.8),move(G['V'],x=344,sx=.8))
G['X']=union(p('M-16 700 L228 721 Q184 657 229 590 L549 105 Q594 35 687 -13 '
               'L431 -14 Q478 35 436 102 L110 586 Q67 659 -16 700 Z'),
             p('M460 673 L657 721 Q567 644 506 557 L163 76 Q132 32 183 -7 L-10 -23 '
               'Q88 34 132 93 L463 563 Q514 635 460 673 Z'))
G['Y']=union(move(G['V'],y=301,sx=.9,sy=.56),stem(223,360,0,125))
G['Z']=p('M38 664 L597 711 L566 649 L148 64 Q409 76 601 157 L545 -20 L-16 -19 '
         'L22 51 L438 633 Q177 613 9 523 Z')

# Lowercase: lower x-height, open counters and restrained joining flourishes.
G['o']=ring(440,488)
G['a']=union(G['o'],stem(333,488,0,102))
G['b']=union(stem(51,719,0,105),move(ring(402,488),x=115))
G['c']=p('M439 493 L392 339 Q356 466 232 438 C55 401 75 85 233 53 Q327 29 424 122 '
         'Q383 -18 215 -15 C-59 3 -69 442 220 494 Q329 516 439 493 Z')
G['d']=union(G['o'],stem(333,686,0,104),p('M329 659 Q382 706 398 840 Q406 725 481 694 L450 653 Z'))
G['e']=union(G['c'],bar(76,218,327,44))
G['f']=union(stem(129,529,-130,110),p('M142 445 L142 570 Q147 785 402 728 L354 571 '
                                   'Q340 708 269 664 Q247 644 252 528 L252 439 Z'),bar(33,440,321,36))
G['g']=union(G['o'],p('M332 489 L472 528 Q437 464 437 356 L437 14 '
                     'C439 -215 188 -285 28 -220 Q236 -191 291 -111 Q339 -51 332 20 Z'))
G['h']=union(stem(46,719,0,105),downstroke(381),
             p('M122 294 Q295 587 445 479 Q492 442 480 303 L382 277 Q398 457 315 441 Q219 419 145 267 Z'))
G['i']=union(stem(59,477,0,101),diamond(116,627,54))
G['j']=union(stem(160,477,80,103),diamond(217,627,54),
             p('M174 119 L264 119 Q273 -135 12 -203 Q174 -101 174 119 Z'))
G['k']=union(stem(48,719,0,106),move(p('M140 211 L332 429 Q355 462 310 483 L490 522 '
               'Q404 462 352 404 L245 287 Q319 306 357 215 Q420 67 529 -31 '
               'Q385 -7 340 44 L229 212 Q187 255 140 191 Z'),x=0))
G['l']=stem(58,722,0,105)
G['m']=union(stem(42,478,0,101),downstroke(338,391,100),downstroke(657,391,100),
             p('M120 294 Q260 572 405 479 Q450 446 438 303 L338 277 Q348 457 285 441 Q197 419 141 267 Z'),
             p('M431 281 Q593 589 723 476 Q772 443 757 290 L664 269 Q679 460 604 438 Q506 410 441 241 Z'))
G['n']=union(stem(46,478,0,103),downstroke(381),
             p('M122 294 Q295 587 445 479 Q492 442 480 303 L382 277 Q398 457 315 441 Q219 419 145 267 Z'))
G['p']=union(stem(51,483,-210,106),move(ring(402,488),x=117))
G['q']=union(G['o'],stem(333,485,-205,101))
G['r']=union(stem(49,480,0,103),p('M135 282 Q270 559 425 477 L355 320 Q292 448 235 383 L141 241 Z'))
G['s']=move(G['S'],sx=.76,sy=.70)
G['t']=union(stem(116,620,0,107),bar(11,439,336,38),
             p('M126 536 L126 637 Q171 687 194 838 Q190 690 272 662 Q223 637 221 568 Z'))
G['u']=union(move(G['U'],sx=.80,sy=.695),stem(397,482,0,85))
G['v']=move(G['V'],sx=.77,sy=.69)
G['w']=move(G['W'],sx=.78,sy=.69)
G['x']=move(G['X'],sx=.77,sy=.69)
G['y']=union(move(G['V'],sx=.8,sy=.69),p('M232 42 L296 130 Q279 -118 67 -207 Q190 -100 232 42 Z'))
G['z']=move(G['Z'],sx=.79,sy=.70)

G['0']=ring(496,680)
G['1']=union(stem(156,675,0,125),p('M14 520 Q160 570 259 727 L265 609 Q155 561 14 520 Z'))
G['2']=p('M20 485 Q13 733 296 711 C603 690 536 466 340 313 L146 127 '
         'Q343 146 512 206 L460 -7 L-6 -7 L35 67 L300 387 C474 584 294 748 151 585 Q94 537 20 485 Z')
G['3']=p('M21 567 Q76 733 271 713 C572 687 481 413 334 376 C583 311 533 31 284 -10 '
         'Q86 -35 2 106 L97 188 Q121 19 290 69 C448 134 366 324 199 323 L127 346 '
         'L168 397 C400 398 448 639 256 648 Q119 654 21 567 Z')
G['4']=union(stem(344,698,0,110),p('M339 732 L378 678 L86 226 L550 262 L529 166 L5 161 L19 211 Z'))
G['5']=p('M58 679 L503 718 L464 571 Q280 635 116 602 L92 416 Q483 533 515 277 '
         'C553 33 239 -110 31 93 L99 204 Q137 34 301 92 C503 188 370 463 73 338 '
         'L43 356 Z')
G['6']=union(move(ring(466,407)),p('M9 202 C-1 456 130 677 422 744 '
                                    'Q242 629 163 427 L143 277 Z'))
G['7']=p('M17 669 L562 707 L536 647 Q334 360 280 -14 L133 -27 '
         'Q220 324 423 614 Q218 637 -4 519 Z')
G['8']=union(move(ring(410,331),x=20,y=365),ring(469,399))
G['9']=move(G['6'],x=466,y=700,sx=-1,sy=-1)

# Punctuation shares the same diamond marks and tapered curves.
G['.']=diamond(65,37,42)
G[',']=union(diamond(65,37,43),p('M83 45 Q137 -40 7 -109 Q58 -41 52 13 Z'))
G[':']=union(G['.'],move(G['.'],y=340))
G[';']=union(G[','],move(G['.'],y=340))
G['!']=union(G['.'],p('M13 708 L134 744 L88 201 L40 179 Z'))
G['?']=union(move(G['.'],x=167),p('M15 555 C6 770 409 793 459 580 Q479 475 336 385 '
            'Q236 327 238 206 L193 184 Q159 330 241 428 C383 594 240 761 105 570 Z'))
G["'"]=p('M19 718 L130 749 L85 493 L39 474 Z')
G['"']=union(G["'"],move(G["'"],x=171))
G['-']=bar(20,269,295,30)
G['_']=bar(12,-55,437,26)
G['=']=union(bar(16,368,399,30),bar(16,207,399,30))
G['+']=union(bar(18,302,436,30),p('M191 106 L191 563 L265 575 L265 100 Z'))
G['/']=p('M-8 -39 L281 742 L357 776 L49 -39 Z')
G['\\']=move(G['/'],x=350,sx=-1)
G['(']=p('M256 791 Q-29 253 243 -185 Q-183 184 256 791 Z')
G[')']=move(G['('],x=270,sx=-1)
G['[']=p('M8 -134 L8 756 L260 756 L260 714 L102 702 L102 -77 L260 -94 L260 -134 Z')
G[']']=move(G['['],x=270,sx=-1)
G['{']=p('M294 774 Q115 757 139 540 Q159 369 43 324 Q158 276 139 106 Q114 -116 294 -136 '
         'Q40 -175 41 87 Q45 272 -29 324 Q45 378 41 544 Q33 802 294 774 Z')
G['}']=move(G['{'],x=300,sx=-1)
G['<']=p('M346 586 L354 515 L80 331 L354 148 L346 80 L-8 312 L-8 350 Z')
G['>']=move(G['<'],x=350,sx=-1)
G['|']=p('M16 -130 L16 770 L80 770 L80 -130 Z')
G['*']=union(diamond(171,497,49),*[move(p('M148 540 L159 741 L208 566 Z'),x=0).transform(
    math.cos(a),math.sin(a),-math.sin(a),math.cos(a),
    171-171*math.cos(a)+497*math.sin(a),497-171*math.sin(a)-497*math.cos(a)) for a in [i*math.pi*2/5 for i in range(5)]])
G['#']=union(move(G['/'],sx=.65),move(G['/'],x=230,sx=.65),bar(-10,453,512,34),bar(-22,203,512,34))
G['%']=union(move(G['/'],x=88),move(ring(183,229),y=455),move(ring(183,229),x=369,y=-3))
G['$']=union(move(G['S'],sx=.83),p('M243 -133 L302 821 L341 824 L287 -112 Z'))
G['&']=union(move(G['8'],sx=1.02),p('M35 455 Q100 624 209 458 L560 57 Q607 5 679 -28 '
           'L502 -17 L141 403 Q87 472 35 455 Z'),p('M401 98 L522 414 L465 458 L654 469 Q563 407 545 359 L452 101 Z'))
G['@']=union(ring(709,714),move(G['a'],x=211,y=195,sx=.60,sy=.65))
G['^']=p('M3 448 L179 722 L243 722 L423 448 L343 453 L201 653 L78 452 Z')
G['`']=p('M0 770 L111 741 L170 570 L128 560 Z')
G['~']=p('M0 300 Q84 469 245 373 Q367 307 447 404 Q392 208 218 319 Q84 391 0 300 Z')
G[' ']=pathops.Path()
G['\u00a0']=pathops.Path()
G['–']=bar(16,269,475,28)
G['—']=bar(16,269,805,28)
G['’']=G["'"]
G['‘']=move(G["'"],x=145,sx=-1)
G['“']=union(G['‘'],move(G['‘'],x=171))
G['”']=G['"']
G['•']=diamond(79,337,56)
G['…']=union(G['.'],move(G['.'],x=174),move(G['.'],x=348))
G['×']=union(move(G['/'],y=107,sx=.85,sy=.61),move(G['\\'],y=107,sx=.85,sy=.61))
G['−']=G['-']

def make_font():
    glyphs={}; metrics={}; cmap={}
    names=['.notdef']
    nd=cut(p('M0 0 L400 0 L400 680 L0 680 Z'),p('M55 55 L345 55 L345 625 L55 625 Z'))
    items=[(None,nd)]+sorted(G.items(),key=lambda item:ord(item[0]))
    manifest={}
    for char, outline in items:
        name='.notdef' if char is None else 'uni%04X'%ord(char)
        if char is not None:
            names.append(name); cmap[ord(char)]=name
        # Subtle forward rhythm; sheared outlines keep standard upright font metrics.
        outline=outline.transform(1,0,.09,1,0,0)
        # Broad-nib weight: strengthen vertical strokes while retaining thin crossbars.
        # Union resolves overlaps before TrueType export; counters stay actual holes.
        if char and char.isalnum():
            outline=union(*(move(outline,x=i*2.5) for i in range(-7,8)))
        b=BoundsPen(None); outline.draw(b)
        if b.bounds:
            xmin,ymin,xmax,ymax=b.bounds
            outline=move(outline,x=30-xmin)
            advance=round(xmax-xmin+60)
            lsb=30
            manifest[char or '.notdef']={'advance':advance,'bounds':[30,round(ymin),round(xmax-xmin+30),round(ymax)]}
        else:
            advance=285; lsb=0
        pen=TTGlyphPen(None)
        outline.draw(Cu2QuPen(pen, max_err=.55, reverse_direction=False))
        glyphs[name]=pen.glyph(); metrics[name]=(advance,lsb)
    fb=FontBuilder(1000,isTTF=True)
    fb.setupGlyphOrder(names); fb.setupCharacterMap(cmap); fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=1000,descent=-300,lineGap=30)
    fb.setupNameTable({'familyName':'Cadence Thorn','styleName':'Regular',
        'uniqueFontIdentifier':'Cadence Thorn Regular 1.000',
        'fullName':'Cadence Thorn Regular','psName':'CadenceThorn-Regular',
        'version':'Version 1.000','copyright':'Cadence Thorn original outlines, 2026.',
        'description':'Fantasy display lettering for Rhythm RPG. Sharp flared strokes and sweeping terminals.'})
    fb.setupOS2(sTypoAscender=1000,sTypoDescender=-300,sTypoLineGap=30,
        usWinAscent=1000,usWinDescent=300,sxHeight=488,sCapHeight=700,
        usWeightClass=600,usWidthClass=5,fsSelection=0x40,fsType=0)
    fb.setupPost(); fb.setupMaxp()
    # Conservative optical pairs: spacing must remain usable outside the logo words.
    pairs={'AV':-65,'AW':-60,'AY':-55,'AT':-35,'VA':-70,'WA':-50,'YA':-60,
           'TA':-42,'To':-48,'Te':-38,'Ty':-45,'Yo':-47,'Vo':-35,'Wa':-30,
           'LT':-20,'LY':-35,'PA':-24,'FA':-30,'Rh':-25,'ry':-12,'rt':-10}
    features='languagesystem DFLT dflt;\nlanguagesystem latn dflt;\nfeature kern {\n'
    for pair,value in pairs.items():
        features+=f'pos {cmap[ord(pair[0])]} {cmap[ord(pair[1])]} {value};\n'
    features+='} kern;'
    addOpenTypeFeaturesFromString(fb.font,features)
    destination=ROOT/'CadenceThorn-Regular.ttf'
    fb.save(destination)
    font=TTFont(destination,checkChecksums=2)
    font.ensureDecompiled()
    missing=[chr(i) for i in range(32,127) if i not in font.getBestCmap()]
    assert not missing, missing
    assert all(g.numberOfContours>=0 for g in font['glyf'].glyphs.values())
    report={'family':'Cadence Thorn','characters':len(cmap),'glyphs':len(names),
            'asciiComplete':not missing,'fileBytes':destination.stat().st_size,'opticalPairs':len(pairs)}
    (ROOT/'Source~'/'build-report.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))

if __name__=='__main__': make_font()

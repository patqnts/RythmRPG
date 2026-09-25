from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parent.parent
REPO=ROOT.parents[2]
FONT=Path(sys.argv[2]) if len(sys.argv)>2 else ROOT/'CadenceThorn-Regular.ttf'
LABEL=REPO/'Assets/TextMesh Pro/Fonts/LiberationSans.ttf'
W,H=1800,1940
image=Image.new('RGB',(W,H),'#101c19'); d=ImageDraw.Draw(image)
cream='#eee5cf'; pale='#a5b4a1'; gold='#cbb888'
def text(value,size,y,color=cream,x=None):
    f=ImageFont.truetype(str(FONT),size)
    bbox=d.textbbox((0,0),value,font=f)
    if x is None: x=(W-d.textlength(value,font=f))/2
    d.text((x,y-bbox[1]),value,font=f,fill=color)
def label(value,y,x=84,size=20,color=pale):
    d.text((x,y),value,font=ImageFont.truetype(str(LABEL),size),fill=color)
def rule(y):d.line((84,y,W-84,y),fill='#344239',width=1)
label('CADENCE THORN   /   FANTASY DISPLAY TYPE',55,color=gold)
label('01   RHYTHM RPG',55,x=1450,size=17)
text('Rhythm RPG',192,133)
text('A God Must Die',116,390,color=gold)
rule(576)
label('COMBAT RANKS',603)
text('SS   S   A   B   C   D',123,661)
rule(834)
label('UPPERCASE',861)
text('A B C D E F G H I J K L M',72,915)
text('N O P Q R S T U V W X Y Z',72,1030)
label('LOWERCASE',1162)
text('a b c d e f g h i j k l m',77,1218)
text('n o p q r s t u v w x y z',77,1340)
rule(1474)
text('0 1 2 3 4 5 6 7 8 9',79,1518)
text('! ? & @ # % + = . , : ; " \' ( ) [ ]',57,1650)
rule(1760)
text('Perfect   Great   Miss   Continue',62,1800)
label('106 CHARACTERS   /   ORIGINAL VECTOR OUTLINES   /   TRUETYPE',1885,size=16)
OUT=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'Source~'/'specimen.png'
image.save(OUT)
print(OUT)

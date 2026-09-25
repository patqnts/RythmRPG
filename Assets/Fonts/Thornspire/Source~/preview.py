import sys
from PIL import Image, ImageDraw, ImageFont
f=sys.argv[1]; out=sys.argv[2]
W=1500
lines=[('Rhythm RPG',150),('A God Must Die',150),('ABCDEFGHIJKLMNOPQRSTUVWXYZ',64),('abcdefghijklmnopqrstuvwxyz',64),('0123456789 !?&.,:;\'"-()[] ÀÉÑ',64)]
H=sum(int(s*1.45) for _,s in lines)+20
im=Image.new('RGB',(W,H),(244,242,236)); d=ImageDraw.Draw(im); y=10
for t,s in lines:
    font=ImageFont.truetype(f,s); d.text((20,y+int(s*0.1)),t,font=font,fill=(10,30,16)); y+=int(s*1.45)
im.save(out)

-- Build a restrained, pixel-aligned idle from the user's separated source layers.
-- Run in Aseprite batch mode from the Unity project directory.
-- The original .aseprite is opened read-only and saved under a new name.
local root=app.params.root or app.fs.currentPath
local source=root..'/Assets/Art/Sprites/Character/Rat King Asset.aseprite'
local output=root..'/Assets/Art/Sprites/Character/Rat King Idle.aseprite'
local proof=root..'/Assets/Art/Sprites/Character/Rat King Animation Source~/Previews'
app.fs.makeAllDirectories(proof)
local s=app.open(source)
assert(s and #s.frames==1,'Expected the separated, single-pose Rat King source.')
assert(s.width==100 and s.height==73,'Review anchors before using a different canvas.')
local original={}
for _,l in ipairs(s.layers) do
  local c=l:cel(1)
  if c then original[l.name]={image=Image(c.image),x=c.position.x,y=c.position.y,layer=l} end
end
for _,name in ipairs({'head','body','clothes','tail'}) do
  assert(original[name],'Missing separated layer: '..name)
end
local round=function(v) return math.floor(v+0.5) end
local function sample(part,x,y)
  x=round(x)-part.x;y=round(y)-part.y
  if x<0 or y<0 or x>=part.image.width or y>=part.image.height then return 0 end
  return part.image:getPixel(x,y)
end
local lifts={0,0,0,1,1,1,2,2,2,2,2,2,2,1,1,1,1,0,0,0,0,0,0,0}
local function animate(part,name,index)
  local image=Image(s.spec)
  local lift=lifts[index]
  local headLift=lifts[math.max(1,index-1)]
  local sway=2*math.sin(2*math.pi*(index-1)/23)
  for y=0,s.height-1 do
    for x=0,s.width-1 do
      local sx,sy=x,y
      if name=='head' then
        sy=y+headLift
      elseif name=='body' or name=='clothes' then
        -- An inverse row map stretches only the upper torso. Feet and robe hems
        -- below y=60 retain their exact original pixels in every frame.
        local anchor=60
        local top=part.y
        if y<anchor then
          sy=top+(y-(top-lift))*(anchor-top)/(anchor-top+lift)
          if name=='body' and sy>=top and sy<anchor then
            local bulge=math.sin(math.pi*(sy-top)/(anchor-top))
            sx=43+(x-43)/(1+(lift/43)*bulge)
          end
        end
      elseif name=='tail' then
        -- The root stays anchored; the upper curl moves at most two pixels.
        local weight=math.max(0,math.min(1,(49-y)/41))^1.3
        sx=x-round(sway*weight)
      end
      image:drawPixel(x,y,sample(part,sx,sy))
    end
  end
  return image
end
app.transaction('Rat King breathing idle',function()
  for i=2,24 do s:newEmptyFrame(i) end
  for i=1,24 do
    s.frames[i].duration=0.100
    for name,part in pairs(original) do
      if part.layer.isVisible then
        local old=part.layer:cel(i)
        if old then s:deleteCel(old) end
        s:newCel(part.layer,i,animate(part,name,i),Point(0,0))
      end
    end
  end
  local tag=s:newTag(1,24)
  tag.name='Idle_Breathing'
  tag.aniDir=AniDir.FORWARD
  tag.repeats=0
end)
s:saveAs(output)

-- Native rendered contact sheet for judging pose continuity.
local sheet=Image(600,292,ColorMode.RGB)
for i=1,24 do
  local frame=Image(s.spec)
  frame:drawSprite(s,i)
  sheet:drawImage(frame,Point(((i-1)%6)*100,math.floor((i-1)/6)*73))
end
sheet:resize(1800,876)
sheet:saveAs(proof..'/RatKing-Idle-ContactSheet.png')
local rest=Image(s.spec);rest:drawSprite(s,1)
local final=Image(s.spec);final:drawSprite(s,24)
assert(rest:isEqual(final),'Loop seam must return exactly to the resting pose.')
local report=io.open(proof..'/animation-validation.txt','w')
report:write('24 frames; 100 ms each; total 2.4 seconds.\n')
report:write('Four visible source layers animated; hidden reference preserved.\n')
report:write('First and last composite frames match exactly.\n')
for i=1,24 do
  local frame=Image(s.spec);frame:drawSprite(s,i)
  for y=60,72 do for x=0,99 do
    assert(frame:getPixel(x,y)==rest:getPixel(x,y),'Ground-contact pixels moved at frame '..i)
  end end
end
report:write('All pixels in rows 60..72 remain identical across the loop.\n')
report:close()
s:close()

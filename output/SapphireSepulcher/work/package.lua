local root = app.params.root
local source = app.open(root .. '/generated-master.png')
source:resize(256,256)
local atlasImage = Image(source)
local tiles = {}
for y=0,7 do
  for x=0,7 do
    tiles[y*8+x+1] = Image(atlasImage, Rectangle(x*32,y*32,32,32))
  end
end
source:close()

local names = {
 'Slate quiet A','Slate quiet B','Slate worn A','Slate worn B','Slate fracture A','Slate fracture B','Diamond carving','Small sapphire diamond',
 'Sapphire horizontal','Sapphire vertical','Sapphire bend A','Sapphire bend B','Sapphire bend C','Sapphire bend D','Sapphire seal','Sapphire intersection',
 'Masonry A','Masonry B','Heraldic wall','Crest wall','Recess wall','Spear wall','Blue ribbed wall','Chained wall',
 'Horizontal coping','Vertical coping','Corner A','Corner B','Corner C','Corner D','Inner junction','Pier cap',
 'Stairs A','Stairs B','Retracted spikes','Raised spikes','Iron grate','Open pit','Sealed hatch','Floor rune',
 'Pillar','Broken pillar','Sentinel statue','Banner','Closed coffer','Open coffer','Altar','Obelisk',
 'Closed door','Open door','Sealed arch','Barred gate','Sarcophagus','Ritual pedestal','Chained monolith','Spear rack',
 'Rubble','Bones','Ceramic shards','Chain coil','Pressure switch','Unlit candle holder','Urn','Stone seal'
}

local function makeSprite(w,h)
 local s = Sprite(w*32,h*32,ColorMode.RGB)
 s.gridBounds = Rectangle(0,0,32,32)
 local empty = s.layers[1]
 app.command.NewLayer{name='Tiles',tilemap=true,gridBounds=Rectangle(0,0,32,32),ask=false}
 local layer = app.layer
 local ts = layer.tileset
 ts.name = 'Sapphire Sepulcher - 32px'
 for i=1,64 do
   local t = s:newTile(ts)
   t.image = tiles[i]
   t.data = names[i]
 end
 s:deleteLayer(empty)
 s.data = 'Sapphire Sepulcher | 32x32 tiles | 32-bit RGBA | Blue material accents | No lighting pass or normal maps included.'
 return s,ts,layer
end
local function tileLayer(s,ts,layer,name,w,h)
 app.sprite=s
 if not layer then
   app.command.NewLayer{name=name,tilemap=true,gridBounds=Rectangle(0,0,32,32),ask=false}
   layer=app.layer
   local generated=layer.tileset
   layer.tileset=ts
   if generated ~= ts then s:deleteTileset(generated) end
 end
 layer.name=name
 local image=Image(w,h,ColorMode.TILEMAP)
 return layer,image
end
local function saveLayer(s,layer,image)
 s:newCel(layer,1,image,Point(0,0))
end
local function put(img,x,y,id)
 img:drawPixel(x,y,id)
end

local atlas,atlasSet,first=makeSprite(8,8)
local categories={'01 Floors','02 Sapphire inlays','03 Wall facades','04 Wall coping','05 Stairs and hazards','06 Architecture and furniture','07 Doors and relics','08 Detail props'}
for row=0,7 do
 local layer,map=tileLayer(atlas,atlasSet,row==0 and first or nil,categories[row+1],8,8)
 for col=0,7 do
  local id=row*8+col+1
  put(map,col,row,id)
  local slice=atlas:newSlice(Rectangle(col*32,row*32,32,32))
  slice.name=string.format('%02d %s',id,names[id])
 end
 saveLayer(atlas,layer,map)
end
atlas:saveAs(root..'/Sapphire-Sepulcher-Tileset.aseprite')
atlas:saveCopyAs(root..'/Sapphire-Sepulcher-Tileset.png')

local w,h=26,23
local room,ts,base=makeSprite(w,h)
local floorLayer,floors=tileLayer(room,ts,base,'01 Slate floors',w,h)
local inlayLayer,inlays=tileLayer(room,ts,nil,'02 Sapphire inlays',w,h)
local wallsLayer,walls=tileLayer(room,ts,nil,'03 Architecture',w,h)
local hazardsLayer,hazards=tileLayer(room,ts,nil,'04 Traps and stairs',w,h)
local propsLayer,props=tileLayer(room,ts,nil,'05 Relics and props',w,h)
local function floorRect(x1,y1,x2,y2)
 for y=y1,y2 do for x=x1,x2 do
  local hash=(x*13+y*7)%19
  put(floors,x,y,hash<15 and (1+(x+y)%2) or 3+(x*y)%4)
 end end
end
floorRect(3,3,22,15)
floorRect(1,6,2,11)
floorRect(23,6,24,11)
floorRect(10,16,15,21)

for x=3,22 do
 put(walls,x,2,((x==5 or x==20) and 20) or ((x==8 or x==17) and 19) or 17+(x%2))
 if x<10 or x>15 then put(walls,x,16,25) end
end
for y=3,15 do
 if y<6 or y>11 then
  put(walls,2,y,26) put(walls,23,y,26)
 else
  put(walls,0,y,26) put(walls,25,y,26)
 end
end
for _,x in ipairs({1,2,23,24}) do put(walls,x,5,25) put(walls,x,12,25) end
for _,p in ipairs({{2,2},{23,2},{2,16},{23,16},{0,5},{25,5},{0,12},{25,12},{9,16},{16,16},{9,22},{16,22}}) do put(walls,p[1],p[2],32) end
for y=17,21 do put(walls,9,y,26) put(walls,16,y,26) end
for x=10,15 do put(walls,x,22,25) end

-- Two ceremonial processional lanes and a transverse seal axis.
for y=4,20 do put(inlays,11,y,10) put(inlays,14,y,10) end
for x=5,20 do put(inlays,x,9,9) end
put(inlays,11,9,16) put(inlays,14,9,16)
for _,p in ipairs({{6,6},{19,6},{6,13},{19,13},{12,6},{13,6}}) do put(inlays,p[1],p[2],15) end
put(inlays,12,9,15) put(inlays,13,9,15)

-- Side trap galleries and the entrance pressure plates.
for _,x in ipairs({5,7,18,20}) do
 for _,y in ipairs({7,11}) do put(hazards,x,y,34+((x+y)%2)) end
end
for _,p in ipairs({{3,8},{22,8},{8,13},{17,13}}) do put(hazards,p[1],p[2],37) end
for x=11,14 do put(hazards,x,21,33) end
put(hazards,12,17,35) put(hazards,13,17,35)

-- Main seal, sentinels, paired reliquaries, and treasure alcoves.
put(props,12,2,51) put(props,13,2,51)
put(props,12,4,47) put(props,13,4,47)
for _,p in ipairs({{5,4},{20,4},{5,14},{20,14}}) do put(props,p[1],p[2],43) end
for _,p in ipairs({{8,5},{17,5},{8,10},{17,10},{10,18},{15,18}}) do put(props,p[1],p[2],41) end
for _,p in ipairs({{9,3},{16,3},{10,20},{15,20}}) do put(props,p[1],p[2],44) end
put(props,1,7,45) put(props,24,7,45)
put(props,1,10,53) put(props,24,10,53)
put(props,6,3,55) put(props,19,3,55)
put(props,12,12,54) put(props,13,12,54)
put(props,3,14,57) put(props,22,14,59)
put(props,4,8,58) put(props,21,11,60)
put(props,12,19,61) put(props,13,19,61)
put(props,3,4,63) put(props,22,4,63)

saveLayer(room,floorLayer,floors)
saveLayer(room,inlayLayer,inlays)
saveLayer(room,wallsLayer,walls)
saveLayer(room,hazardsLayer,hazards)
saveLayer(room,propsLayer,props)
room:saveAs(root..'/Sapphire-Sepulcher-Dungeon.aseprite')
room:saveCopyAs(root..'/Sapphire-Sepulcher-Dungeon.png')

-- Reopen both native files and compare their rendered pixels with exported PNGs.
for _,stem in ipairs({'Sapphire-Sepulcher-Tileset','Sapphire-Sepulcher-Dungeon'}) do
 local saved=app.open(root..'/'..stem..'.aseprite')
 local exported=Image{fromFile=root..'/'..stem..'.png'}
 assert(Image(saved):isEqual(exported),'Round-trip pixels mismatch: '..stem)
 assert(#saved.tilesets==1,'Expected one shared tileset')
 assert(#saved.tilesets[1]==65,'Expected 64 art tiles plus empty tile')
 for _,l in ipairs(saved.layers) do assert(l.isTilemap,'Expected editable tilemap layer') end
 print(stem..': verified '..saved.width..'x'..saved.height..', '..#saved.layers..' tilemap layers, 64 tiles')
 saved:close()
end
print('Packaging and native Aseprite round-trip verification complete.')

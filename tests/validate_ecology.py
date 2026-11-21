from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
for xml in (root/'Defs').rglob('*.xml'): ET.parse(xml)
orchid=ET.parse(root/'Defs/ThingDefs/GU_MoonOrchidHabitat.xml')
grass=ET.parse(root/'Defs/ThingDefs/GU_SpiritFlora.xml')
spring=ET.parse(root/'Defs/ThingDefs/GU_SpiritSpring.xml')
assert orchid.find('.//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]') is not None
assert grass.find('.//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]') is not None
assert spring.find('.//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritSpring"]') is not None
source=(root/'Source/ProjectGu/Springs/CompGuSpiritSpring.cs').read_text()
for legacy in ('guNextProductionTick','guLifetimeProduced','guHopeGuAvailable','guNextHopeRecoveryTick','guHabitatPatches'):
    assert legacy in source, f'Legacy save field removed: {legacy}'
for new in ('guReserve024','guMatureHope024','guJuvenileHope024','guNextEcology024'):
    assert new in source
for name in ('GuEcologyMapComponent.cs','CompGuSpiritFlora.cs','CompProperties_GuSpiritFlora.cs'):
    assert (root/'Source/ProjectGu/Springs'/name).is_file()
for asset in ('MoonOrchid/ColonyA.png', 'MoonOrchid/ColonyB.png', 'SpiritGrass/PatchA.png',
              'SpiritGrass/PatchB.png', 'SpiritGrass/PatchC.png', 'SpiritGrass/PatchD.png'):
    assert (root/'Textures/ProjectGU'/asset).is_file()
print('PASS: all defs parse; generic flora and spring comps declared; save keys preserved; ecology source/assets exist.')

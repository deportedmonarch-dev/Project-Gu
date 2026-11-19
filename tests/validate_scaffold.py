"""Static checks. Does not compile code or run RimWorld."""
from pathlib import Path
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
about = ET.parse(root/'About/About.xml').getroot()
hediff = ET.parse(root/'Defs/HediffDefs/GU_Aperture.xml').getroot()
project = ET.parse(root/'Source/ProjectGu/ProjectGu.csproj').getroot()
assert about.findtext('packageId') == 'projectgu.livingcultivation'
assert [x.text for x in about.findall('./supportedVersions/li')] == ['1.7']
assert project.findtext('./PropertyGroup/TargetFramework') == 'net10.0'
assert project.findtext('./PropertyGroup/OutputPath') == '../../1.7/Assemblies/'
assert {x.attrib['Include'] for x in project.findall('./ItemGroup/Reference')} == {'Assembly-CSharp','Waylith','UnityStubs'}
assert hediff.find('./HediffDef/defName').text == 'GU_Aperture'
assert hediff.find('./HediffDef/hediffClass').text == 'ProjectGu.Hediff_Aperture'
assert (root/'Source/ProjectGu/Hediff_Aperture.cs').exists()
assert (root/'Source/ProjectGu/GuWorldComponent.cs').exists()
assert not (root/'Assemblies/ProjectGu.dll').exists()
print('PASS: 1.7 metadata, net10.0 references, XML, sources, and folder structure.')

assert (root/'Source/ProjectGu/GuAwakeningService.cs').exists()
assert (root/'Source/ProjectGu/Domain/GuPawnRecord.cs').exists()
assert (root/'docs/TEST-PLAN-0.2.0.md').exists()
print('PASS: 0.2.0 source and test plan present')

assert (root/'Source/ProjectGu/Springs/CompGuSpiritSpring.cs').exists()
assert (root/'docs/TEST-PLAN-0.2.1.md').exists()
ET.parse(root/'Defs/ThingDefs/GU_SpiritSpring.xml')
print('PASS: 0.2.1 spring definitions and production source present.')

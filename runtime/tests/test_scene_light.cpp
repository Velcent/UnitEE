// The baked-lighting material flag (ADR-014) and the light payload it
// leaves alone.
//
// Bytes are written from docs/formats/p2b-container.md rather than from the
// C# exporter, the discipline every SCEN test follows: a drift on either
// side fails here instead of in PCSX2. The baked flag is MATL bit5; bits 3
// and 4 belong to M14's clamp addressing and sky, and a baked record must
// not be mistaken for either.
#include "ps2ur/p2b_scene.h"

#include <gtest/gtest.h>

#include <cstring>
#include <vector>

using namespace ps2ur;
using namespace ps2ur::scene;

namespace {

void put_u16(std::vector<uint8_t>& v, uint16_t x)
{
    v.push_back(static_cast<uint8_t>(x));
    v.push_back(static_cast<uint8_t>(x >> 8));
}

void put_u32(std::vector<uint8_t>& v, uint32_t x)
{
    v.push_back(static_cast<uint8_t>(x));
    v.push_back(static_cast<uint8_t>(x >> 8));
    v.push_back(static_cast<uint8_t>(x >> 16));
    v.push_back(static_cast<uint8_t>(x >> 24));
}

void put_i32(std::vector<uint8_t>& v, int32_t x)
{
    put_u32(v, static_cast<uint32_t>(x));
}

void put_f32(std::vector<uint8_t>& v, float f)
{
    uint32_t bits;
    std::memcpy(&bits, &f, 4);
    put_u32(v, bits);
}

// One root entity, carrying a 24-byte directional light when asked (the
// loader refuses a scene with no entities, so the entity stays either way).
std::vector<uint8_t> build_scene(bool with_light)
{
    std::vector<uint8_t> b;
    put_u32(b, 1); // entity_count
    put_u32(b, with_light ? 1u : 0u); // component_count
    put_u32(b, 0); // name table

    put_i32(b, -1);   // parent
    put_f32(b, 0.0f); // pos
    put_f32(b, 0.0f);
    put_f32(b, 0.0f);
    put_f32(b, 0.0f); // rot
    put_f32(b, 0.0f);
    put_f32(b, 0.0f);
    put_f32(b, 1.0f);
    put_f32(b, 1.0f); // scale
    put_f32(b, 1.0f);
    put_f32(b, 1.0f);
    put_u32(b, 0); // name hash
    put_u16(b, 0); // layer
    put_u16(b, 0); // tag
    put_u32(b, 0); // flags
    put_u32(b, 0); // component_first
    put_u16(b, with_light ? 1u : 0u); // component_count
    put_u16(b, 0); // pad
    if (!with_light) {
        return b;
    }

    const uint32_t refs_at = static_cast<uint32_t>(b.size());
    put_u16(b, kComponentDirectionalLight);
    put_u16(b, 0);
    put_u32(b, refs_at + 8u);

    put_f32(b, 0.0f); // dir
    put_f32(b, -1.0f);
    put_f32(b, 0.0f);
    put_f32(b, 0.9f); // colour
    put_f32(b, 0.8f);
    put_f32(b, 0.7f);
    return b;
}

// A MATL v2 section of 48-byte records: (kind, flags) pairs.
std::vector<uint8_t> build_materials(
    const std::vector<std::pair<uint32_t, uint32_t>>& records)
{
    std::vector<uint8_t> b;
    for (const auto& r : records) {
        put_u32(b, r.first);      // kind
        put_u32(b, 0xFFFFFFFFu);  // texture
        put_f32(b, 1.0f);         // tint
        put_f32(b, 1.0f);
        put_f32(b, 1.0f);
        put_f32(b, 1.0f);
        put_u32(b, 0);            // gs_test
        put_u32(b, 0);
        put_u32(b, 0);            // gs_alpha
        put_u32(b, 0);
        put_u32(b, r.second);     // flags
        put_u32(b, 0);            // pad
    }
    return b;
}

std::vector<uint8_t> wrap_sections(
    const std::vector<std::pair<uint32_t, std::vector<uint8_t>>>& sections)
{
    const uint32_t count = static_cast<uint32_t>(sections.size());
    uint32_t offset = 2048;
    std::vector<uint32_t> offsets;
    for (const auto& s : sections) {
        offsets.push_back(offset);
        offset += (static_cast<uint32_t>(s.second.size()) + 2047u) & ~2047u;
    }
    std::vector<uint8_t> file(offset, 0);
    const char magic[4] = {'P', '2', 'B', 'C'};
    std::memcpy(file.data(), magic, 4);
    file[4] = 1; // version_major
    const uint32_t total = static_cast<uint32_t>(file.size());
    std::memcpy(file.data() + 8, &total, 4);
    std::memcpy(file.data() + 12, &count, 4);
    for (uint32_t i = 0; i < count; ++i) {
        uint8_t* entry = file.data() + 32 + i * 32;
        const uint32_t type = sections[i].first;
        const uint32_t size = static_cast<uint32_t>(sections[i].second.size());
        const uint32_t checksum = io::crc32(sections[i].second.data(), size);
        std::memcpy(entry + 0, &type, 4);
        std::memcpy(entry + 4, &offsets[i], 4);
        std::memcpy(entry + 8, &size, 4);
        std::memcpy(entry + 12, &size, 4);
        std::memcpy(entry + 16, &checksum, 4);
        std::memcpy(file.data() + offsets[i], sections[i].second.data(), size);
    }
    return file;
}

bool load(World& world, const std::vector<uint8_t>& file)
{
    io::P2bFile parsed;
    if (!parsed.parse(file.data(), static_cast<uint32_t>(file.size()))) {
        return false;
    }
    return world.load(parsed);
}

} // namespace

TEST(SceneLight, TheTwentyFourByteLightPayloadStillLoads)
{
    const auto file = wrap_sections({{io::kSectionScene, build_scene(true)}});
    static World world;
    ASSERT_TRUE(load(world, file)) << world.error();
    ASSERT_TRUE(world.has_light());
    EXPECT_NEAR(world.light().colour.x, 0.9f, 1e-6f);
    EXPECT_NEAR(world.light().dir.y, -1.0f, 1e-6f);
}

TEST(SceneLight, MaterialFlagBit5MarksBakedLightingAndNothingElse)
{
    const auto file = wrap_sections({
        {io::kSectionMaterial,
         build_materials({{kMaterialVertexLitFog, 1u | kMaterialFlagBaked},
                          {kMaterialUnlitTextured, 1u},
                          {kMaterialUnlit, kMaterialFlagBaked},
                          {kMaterialUnlitTextured, 1u | 8u | 16u}})},
        {io::kSectionScene, build_scene(false)},
    });
    static World world;
    ASSERT_TRUE(load(world, file)) << world.error();
    ASSERT_EQ(world.material_count(), 4u);
    EXPECT_TRUE(world.material(0).baked);
    EXPECT_TRUE(world.material(0).zwrite);
    EXPECT_FALSE(world.material(0).clamp);
    EXPECT_FALSE(world.material(0).sky);
    EXPECT_EQ(world.material(0).kind, kMaterialVertexLitFog);
    EXPECT_FALSE(world.material(1).baked);
    EXPECT_TRUE(world.material(2).baked);
    EXPECT_FALSE(world.material(2).zwrite);
    // M14's clamp and sky bits are not the baked bit.
    EXPECT_FALSE(world.material(3).baked);
    EXPECT_TRUE(world.material(3).clamp);
    EXPECT_TRUE(world.material(3).sky);
}

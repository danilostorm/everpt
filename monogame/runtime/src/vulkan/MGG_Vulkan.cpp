// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

#include "api_MGG.h"

#include "mg_common.h"

#include "AlphaTestEffect.vk.mgfxo.h"
#include "BasicEffect.vk.mgfxo.h"
#include "DualTextureEffect.vk.mgfxo.h"
#include "EnvironmentMapEffect.vk.mgfxo.h"
#include "SkinnedEffect.vk.mgfxo.h"
#include "SpriteEffect.vk.mgfxo.h"
#include "mg_effect.h"

// Etervale fork: the enum conversions return in every case and assert in the default; the compiler must also know the default
// cannot fall off the end of the function (MSVC C4715, GCC/Clang -Wreturn-type), which would be undefined behaviour.
#if defined(_MSC_VER)
#define MGVK_UNREACHABLE() do { assert(0); __assume(0); } while (0)
#else
#define MGVK_UNREACHABLE() do { assert(0); __builtin_unreachable(); } while (0)
#endif

#include <unordered_map>
#include <algorithm>
#include <map>
#include <deque>
#include <vector>
#include <thread>
#include <string>

#define VULKAN_HPP_DISPATCH_LOADER_DYNAMIC 1
#define VULKAN_HPP_NO_EXCEPTIONS
#define VULKAN_HPP_TYPESAFE_CONVERSION 1


#if defined(__APPLE__)
#include <MoltenVK/mvk_vulkan.h>
#else
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan.h>
#endif

#ifdef DEBUG
#define VK_EXT_debug_utils
#ifdef __APPLE__
#define MVK_CONFIG_DEBUG
#endif
#endif

#ifndef __APPLE__
#define VOLK_IMPLEMENTATION
#include <volk.h>
#endif

#define VMA_IMPLEMENTATION
#define VMA_STATIC_VULKAN_FUNCTIONS 1
#include <vk_mem_alloc.h>

#if defined(MG_SDL2)
#include <SDL_vulkan.h>
#include <SDL_syswm.h>
#include <SDL_events.h>
#endif

#ifdef _WIN32
#define NOMINMAX
#include <Windows.h>
#include <vulkan/vulkan_win32.h>
#elif defined(__APPLE__)
#include <CoreGraphics/CoreGraphics.h>
#include <vulkan/vulkan_macos.h>
#endif

#define VK_CHECK_RESULT(vkr)															\
{																						\
	VkResult _vkr = vkr;																\
	if (_vkr < VK_SUCCESS)																\
	{																					\
		fprintf(stderr, "Fatal : VkResult is %x in %s:%d", _vkr, __FILE__, __LINE__);	\
		assert(_vkr == VK_SUCCESS);														\
	}																					\
}

// Makes sense for 2026.
#define MGVK_MAX_MIPS 16

// TODO: We should expose this to C# somehow.
bool MGVK_ValidationEnabled = false;


#if defined(DEBUG)
template <typename... FmtArgs>
static void setObjectNameVariadic(
    VkDevice device, uint64_t object, VkObjectType type,
    const char* file, int line,
    const char* nameOrFormat, FmtArgs&&... fmtArgs)
{
    if (!device || !object || !vkSetDebugUtilsObjectNameEXT) return;

    const char* baseName = nullptr;
    char formattedNameBuffer[256];

    if constexpr (sizeof...(FmtArgs) > 0)
    {
        snprintf(formattedNameBuffer, sizeof(formattedNameBuffer), nameOrFormat, std::forward<FmtArgs>(fmtArgs)...);
        baseName = formattedNameBuffer;
    }
    else
    {
        baseName = nameOrFormat;
    }

    char finalName[256];
    snprintf(finalName, sizeof(finalName), "%s (%s:%d)", baseName, file, line);

    VkDebugUtilsObjectNameInfoEXT info = {};
    info.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_OBJECT_NAME_INFO_EXT;
    info.pNext = VK_NULL_HANDLE;
    info.objectType = type;
    info.objectHandle = object;
    info.pObjectName = finalName;

    vkSetDebugUtilsObjectNameEXT(device, &info);
}

#define VK_SET_OBJECT_NAME(device, object, type, ...) \
    setObjectNameVariadic(device, (uint64_t)(object), type, __FILE__, __LINE__, __VA_ARGS__)
#else
#define VK_SET_OBJECT_NAME(...) ((void)0)
#endif

template<class T>
T MG_AlignUp(T value, const T alignment)
{
	return (value + alignment - 1) & ~(alignment - 1);
}

struct MGVK_Program;

typedef uint32_t FrameCounter;


constexpr size_t MGVK_NUM_TARGETS = 4;

struct MGVK_TargetSet
{
    MGG_Texture* targets[MGVK_NUM_TARGETS] = { 0 };
	bool firstUse[MGVK_NUM_TARGETS] = { false };
	int numTargets = 0;
	std::optional<int> arraySlices[MGVK_NUM_TARGETS];
};

struct MGVK_TargetSetCache
{
	MGVK_TargetSet set;
	uint64_t uid = 0;	// Etervale fork: never reused, unlike the address (pipeline keys)

	int width = 0;
	int height = 0;
	VkFramebuffer framebuffer = VK_NULL_HANDLE;
	VkRenderPass renderPass = VK_NULL_HANDLE;
    std::optional<VkImageView> arraySlicesViews[MGVK_NUM_TARGETS];
	int colorAttachments = 0;	// Etervale fork: 0 for a depth surface (issue 016); the pipelines' blend attachment count
};

struct MGVK_PipelineState
{
	VkPrimitiveTopology topology = VK_PRIMITIVE_TOPOLOGY_POINT_LIST;

	MGVK_Program* program = nullptr;
	MGG_InputLayout* layout = nullptr;

	MGVK_TargetSetCache* targets = nullptr;

	MGG_BlendState* blendState = nullptr;
	MGG_RasterizerState* rasterizerState = nullptr;
	MGG_DepthStencilState* depthStencilState = nullptr;
};

struct MGVK_PipelineCache
{
	MGVK_PipelineState state;
	VkPipeline cache = VK_NULL_HANDLE;
};

// Etervale fork: the identity of a pipeline. The objects' ids are never reused, so a pipeline built for a destroyed
// object can never be returned for a new one allocated at the same address, and the key is compared in full: a 32-bit
// hash alone returned the wrong pipeline on a collision.
struct MGVK_PipelineKey
{
	uint64_t program = 0;
	uint64_t layout = 0;
	uint64_t targets = 0;
	uint64_t blend = 0;
	uint64_t rasterizer = 0;
	uint64_t depthStencil = 0;
	uint32_t topology = 0;
	uint32_t padding = 0;

	bool operator==(const MGVK_PipelineKey& other) const
	{
		return memcmp(this, &other, sizeof(MGVK_PipelineKey)) == 0;
	}
};

struct MGVK_PipelineKeyHash
{
	size_t operator()(const MGVK_PipelineKey& key) const;
};

struct MGVK_Program
{
	uint64_t uid;		// Etervale fork: the program id (vertex and pixel shader ids)
	MGG_Shader* vertex;
	MGG_Shader* pixel;
	VkPipelineLayout layout;
};

struct MGVK_Frame
{
	bool is_recording = false;
	bool is_rendering = false;
	uint32_t image_index = -1;
	uint32_t uniformOffset = 0;
	MGG_Buffer* uniforms = nullptr;
	VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
	VkSemaphore imageAcquiredSemaphore = VK_NULL_HANDLE;
	VkFence completedFence = VK_NULL_HANDLE;
	// Etervale fork: a submission of this frame's commands before Present (MGVK_FlushCommands) already waited for the
	// image acquisition; Present must not wait for the semaphore again.
	bool acquireWaited = false;
};

struct MGVK_Swapchain
{
	MGG_Texture* texture = nullptr;
	VkSemaphore renderCompleteSemaphore = VK_NULL_HANDLE;
};

struct MGG_GraphicsAdapter
{
	VkInstance instance = nullptr;
	VkPhysicalDevice device = nullptr;
	VkPhysicalDeviceProperties properties = { 0 };
	VkPhysicalDeviceFeatures features = { 0 };
	VkPhysicalDeviceMemoryProperties memory = { 0 };

	MGG_DisplayMode current = { MGSurfaceFormat::Color, 0, 0 };

	std::vector<MGG_DisplayMode> modes;
};

struct MGG_Buffer
{
	FrameCounter frame = 0;

	MGG_Buffer* next = nullptr;

	MGBufferType type = MGBufferType::Vertex;

	int dataSize = 0;
	int actualSize = 0;
	bool dirty = false;

	uint8_t* push = nullptr;

	VkBuffer buffer = VK_NULL_HANDLE;
	VmaAllocation allocation = VK_NULL_HANDLE;

	uint8_t* mapped = nullptr;

	// Etervale fork: set once a draw has read the buffer; `frame` is then the last frame that did.
	bool used = false;

	// Etervale fork: the id of a storage buffer (MGBufferType::Storage), never reused, for the descriptor-set keys.
	uint64_t id = 0;
};

struct MGVK_Transfer
{
	MGG_Texture* owner;
	VkCommandBuffer cmd;
	MGG_Buffer staging;
	VkFence done;
};


const int MAX_TEXTURE_SLOTS = 16;

// Etervale fork: shader storage resources (monogame/issues/005, 006). The effect compiler shifts the HLSL registers into
// one descriptor set per stage: b0 → binding 0, t/s → 32 + slot (textures, samplers and the read-only buffers,
// StructuredBuffer and ByteAddressBuffer), u → 64 + slot (RWTexture2D, RWStructuredBuffer, RWByteAddressBuffer).
const uint32_t MGVK_TEXTURE_BINDING = 32;
const uint32_t MGVK_UAV_BINDING = 64;
const int MGVK_MAX_UAV_SLOTS = 8;

// What a u-register holds: a storage buffer, or one mip of a storage image.
struct MGVK_UavBinding
{
	MGG_Buffer* buffer = nullptr;
	MGG_Texture* texture = nullptr;
	mgint mip = 0;
};

// Etervale fork: asynchronous frame capture (MGG_FrameCapture_*). Each swapchain frame slot copies its presented image
// (downscaled by a blit) into its own host buffer inside the frame's command buffer; the copy is read when the slot's
// fence has already been waited for reuse, so capturing adds no synchronisation of its own.
struct MGVK_CaptureSlot
{
	VkImage image = VK_NULL_HANDLE;
	VmaAllocation imageAllocation = VK_NULL_HANDLE;
	VkBuffer buffer = VK_NULL_HANDLE;
	VmaAllocation bufferAllocation = VK_NULL_HANDLE;
	void* mapped = nullptr;
	uint32_t width = 0;
	uint32_t height = 0;
	bool pending = false;
	bool drawn = false;
	uint64_t frame = 0;
};

struct MGVK_CapturedFrame
{
	uint64_t frame = 0;
	uint32_t width = 0;
	uint32_t height = 0;
	bool drawn = false;
	std::vector<uint8_t> rgba;
};

struct MGG_GraphicsDevice
{
	VkInstance instance = VK_NULL_HANDLE;

	VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
	VkPhysicalDeviceProperties deviceProperties;
	VkPhysicalDeviceFeatures deviceFeatures;
	VkPhysicalDeviceMemoryProperties deviceMemoryProperties;
	bool customBorderColorSupported = false;

	// Etervale fork (issue 012): VK_KHR_pipeline_executable_properties, enabled when MG_PIPELINE_STATISTICS=1 at device
	// creation and the driver has it: pipelines are then created with their statistics captured, and the last bound
	// graphics and compute pipelines are kept for MGG_GraphicsDevice_GetPipelineStatistics.
	bool pipelineStatistics = false;
	VkPipeline lastGraphicsPipeline = VK_NULL_HANDLE;
	VkPipeline lastComputePipeline = VK_NULL_HANDLE;

	VkDevice device = VK_NULL_HANDLE;

	std::recursive_mutex queueMutex;
	uint32_t graphicsQueueFamily = 0;
	VkQueue queue = VK_NULL_HANDLE;
	VkCommandPool cmdPool = VK_NULL_HANDLE;

	std::recursive_mutex transferMutex;
	VkCommandPool transferPool = VK_NULL_HANDLE;
	std::vector<MGVK_Transfer> pendingTransfers;

	VmaAllocator allocator = VK_NULL_HANDLE;

	FrameCounter frame = 0;
	FrameCounter frameIndex = 0;
	FrameCounter freeFrames = 0;
	FrameCounter swapchainCount = 0;

	std::vector<MGVK_Frame> frames;
	std::vector<MGVK_Swapchain> swapchains;

	uint32_t swapchainWidth = 0;
	uint32_t swapchainHeight = 0;
	VkFormat colorFormat = VK_FORMAT_UNDEFINED;
	VkFormat depthFormat = VK_FORMAT_UNDEFINED;
	int32_t multiSampleCount = 0;

	bool inRenderPass = false;
	bool renderTargetDirty = false;

	VkViewport viewport = { 0 };
	VkRect2D scissor = { 0 };
	bool scissorDirty = false;

	// Etervale fork: set when the application chose the viewport / scissor rectangle after the last SetRenderTargets. The
	// render pass begins lazily at the next draw, and its full-target defaults must not replace what was chosen.
	bool viewportSet = false;
	bool scissorSet = false;

#if defined(MG_SDL2)
	SDL_Window* window = nullptr;
#else
#error Not Implemented
#endif
	VkSurfaceKHR surface = VK_NULL_HANDLE;
	VkSwapchainKHR swapchain = VK_NULL_HANDLE;
	int syncInterval = 0;

	uint64_t vertexBuffersDirty = 0xFFFFFFFF;
	MGG_Buffer* vertexBuffers[8] = { 0 };
	uint32_t vertexOffsets[8] = { 0 };

	MGG_Buffer* indexBuffer = nullptr;
	MGIndexElementSize indexBufferSize = MGIndexElementSize::SixteenBits;

	uint64_t currentTextureId = 1;
	uint64_t currentSamplerId = 1;
	MGG_Texture* textures[(mgint)MGShaderStage::Count][MAX_TEXTURE_SLOTS] = { 0 };
	MGG_SamplerState* samplers[(mgint)MGShaderStage::Count][MAX_TEXTURE_SLOTS] = { 0 };
	uint32_t textureSamplerDirty = 0;
	MGG_Texture* nullTexture[3];

	bool blendFactorDirty = false;
	float blendFactor[4] = { 0 };

	uint32_t currentShaderId = 0;
	MGG_Shader* shaders[(mgint)MGShaderStage::Count] = { 0 };
	bool shaderDirty = false;
	std::map<uint64_t, MGVK_Program*> shader_programs;
	std::vector<MGG_Shader*> all_shaders;

	VkPipelineCache pipelineCache = VK_NULL_HANDLE;
	// Etervale fork: exact-match caches (change 6). The hash only picks the bucket; entries are compared in full.
	std::unordered_map<MGVK_PipelineKey, MGVK_PipelineCache, MGVK_PipelineKeyHash> pipelines;

	std::unordered_multimap<uint32_t, MGG_BlendState*> blendStates;
	std::unordered_multimap<uint32_t, MGG_RasterizerState*> rasterizerStates;
	std::unordered_multimap<uint32_t, MGG_DepthStencilState*> depthStencilStates;

	MGVK_TargetSet targets;
	std::unordered_multimap<uint32_t, MGVK_TargetSetCache*> targetCache;

	// Etervale fork: ids of cached objects, never reused.
	uint64_t nextUid = 1;


	//
	bool pipelineStateDirty = false;
	MGVK_PipelineState pipelineState;

	MGG_Buffer* uniforms[(mgint)MGShaderStage::Count] = { nullptr };	// Etervale fork: one per stage, compute included
	uint32_t uniformsDirty = 0;

	// Etervale fork: storage resources per stage (issues 005, 006): read-only buffers by t-slot, UAVs by u-slot, and per
	// stage the slots changed since the shader's descriptor set was last chosen (bits 0-15 t-slots, 16-23 u-slots).
	MGG_Buffer* readBuffers[(mgint)MGShaderStage::Count][MAX_TEXTURE_SLOTS] = { };
	MGVK_UavBinding uavs[(mgint)MGShaderStage::Count][MGVK_MAX_UAV_SLOTS] = { };
	uint32_t storageDirty[(mgint)MGShaderStage::Count] = { };
	// Bound where a read-only buffer slot is empty, so a descriptor is never left unwritten (16 zero bytes).
	MGG_Buffer* nullStorageBuffer = nullptr;

	VkDescriptorSet descriptorSets[2] = { 0 };
	uint32_t dynamicOffsets[2] = { 0 };

	// Some needed limits.
	VkDeviceSize minUniformBufferOffsetAlignment = 0;

	std::queue<MGG_Buffer*> destroyBuffers;
	std::queue<MGG_Texture*> destroyTextures;
	std::queue<MGG_BlendState*> destroyBlendStates;
	std::queue<MGG_RasterizerState*> destroyRasterizerStates;
	std::queue<MGG_DepthStencilState*> destroyDepthStencilStates;
	std::queue<MGG_SamplerState*> destroySamplers;

	MGG_Buffer* discarded = nullptr;
	MGG_Buffer* pending = nullptr;
	MGG_Buffer* free = nullptr;

	std::vector<MGG_Buffer*> all_buffers;
	std::vector<MGG_Texture*> all_textures;

	std::vector<MGG_OcclusionQuery*> deferredOcclusionQueries;

	// Etervale fork: staging buffers of texture uploads recorded in a frame's command buffer, freed once that frame is done.
	std::vector<std::pair<FrameCounter, MGG_Buffer>> frameStaging;
	std::thread::id renderThread;

	// Etervale fork: asynchronous frame capture.
	bool captureEnabled = false;
	int captureDownscale = 1;
	size_t captureMaxQueued = 0;
	uint64_t captureDropped = 0;
	std::vector<MGVK_CaptureSlot> captureSlots;
	std::deque<MGVK_CapturedFrame> captureReady;
};

struct MGG_Texture
{
	FrameCounter writeFrame = -1;
	FrameCounter frame = 0;
	// Etervale fork: the frame whose command buffer holds an upload into this texture (MGG_Texture_SetData), -1 if none.
	FrameCounter uploadFrame = -1;

	MGTextureType type = MGTextureType::_2D;
	MGSurfaceFormat format = MGSurfaceFormat::Color;

	MGRenderTargetUsage usage = MGRenderTargetUsage::PlatformContents;

	mgbool isTarget = false;
	mgbool isSwapchain = false;
	mgbool isExternal = false;

	mgint multiSampleCount = 0;

	uint64_t id = 0;
	VkImageCreateInfo info;

	VkImage image = VK_NULL_HANDLE;
	VmaAllocation allocation = VK_NULL_HANDLE;

	VkImage msImage = VK_NULL_HANDLE;
	VmaAllocation msAllocation = VK_NULL_HANDLE;

	VkImageLayout layouts[MGVK_MAX_MIPS];
	VkImageLayout optimal_layout = VK_IMAGE_LAYOUT_GENERAL;

	uint64_t lastWriteTimeline = 0;

	void* mappedAddr = nullptr;

	VkImageView view = VK_NULL_HANDLE;
	VkImageView target_view = VK_NULL_HANDLE;
	VkImageView resolve_view = VK_NULL_HANDLE;

	MGDepthFormat depthFormat = MGDepthFormat::None;
	MGG_Texture* depthTexture = nullptr;

	// Etervale fork: a storage image (MGG_Texture_CreateStorage, issue 005): shaders write it through a view of one mip,
	// created when first bound.
	bool isStorage = false;
	VkImageView storage_views[MGVK_MAX_MIPS] = { };
};

struct MGG_InputLayout
{
	uint64_t uid = 0;	// Etervale fork: never reused, unlike the address (pipeline keys)
	std::vector<VkVertexInputAttributeDescription> attributes;
	std::vector<VkVertexInputBindingDescription> bindings;
};

struct MGVK_DescriptorInfo
{
	FrameCounter frame;
	VkDescriptorSet set;
	// Etervale fork: what the set was written for (frame index, slot masks, texture and sampler ids per used slot), so a
	// lookup compares the full key and a 32-bit hash collision cannot hand back a set with another draw's textures.
	std::vector<uint64_t> key;
};


// The location of a vertex shader input, by semantic (see MGVK_ReflectInputLocations).
struct MGVK_InputLocation
{
	MGVertexElementUsage usage;
	mguint index;		// semantic index of the input's first location
	uint32_t location;
};

struct MGG_Shader
{
	uint32_t id;

	MGShaderStage stage;

	// Vertex shaders: the SPIR-V location of each input, matched to the vertex elements by semantic.
	std::vector<MGVK_InputLocation> inputLocations;

	VkShaderModule module;
	VkDescriptorSetLayout setLayout;

	std::vector<VkDescriptorSetLayoutBinding> bindings;

	VkWriteDescriptorSet* writes;
	VkDescriptorBufferInfo* bufferInfo;
	VkDescriptorImageInfo* imageInfo;
	MGTextureType textureTypes[MAX_TEXTURE_SLOTS];

	mguint uniformSlots;
	mguint textureSlots;
	mguint samplerSlots;

	// Etervale fork (issues 005, 006), derived from the bindings: t-slots holding a read-only storage buffer, u-slots, and
	// the u-slots holding a storage image. A compute shader also has its pipeline (it depends on nothing else).
	uint32_t bufferSlots;
	uint32_t uavSlots;
	uint32_t uavImageSlots;
	VkPipelineLayout computeLayout;
	VkPipeline computePipeline;

	VkDescriptorPoolCreateInfo* poolInfo;
	VkDescriptorPool pool;

	std::queue<MGVK_DescriptorInfo*> freeSets;
	std::multimap<uint32_t, MGVK_DescriptorInfo*> usedSets;   // Etervale fork: hash → sets, key compared in full
};

struct MGVK_BindingInfo
{
	VkDescriptorType type;
	mguint slot;
	mguint binding;
	MGG_Shader* shader;
};

struct MGG_BlendState
{
	FrameCounter frame;
	mguint hash;
	mgint refs;
	uint64_t uid;					// Etervale fork: never reused (pipeline keys)
	MGG_BlendState_Info source[4];	// Etervale fork: what it was created from (exact cache match)
	VkPipelineColorBlendStateCreateInfo info;
	VkPipelineColorBlendAttachmentState attachments[4];
};

struct MGG_DepthStencilState
{
	FrameCounter frame;
	mguint hash;
	mgint refs;
	uint64_t uid;
	MGG_DepthStencilState_Info source;
	VkPipelineDepthStencilStateCreateInfo info;
};

struct MGG_RasterizerState
{
	FrameCounter frame;
	mguint hash;
	mgint refs;
	uint64_t uid;
	MGG_RasterizerState_Info source;
	VkPipelineRasterizationStateCreateInfo info;
	mgbool scissorTestEnable;
	mgbool multiSampleAntiAlias;
};

struct MGG_SamplerState
{
	uint64_t id;
	VkSampler sampler;
	MGG_SamplerState_Info info;
};

struct MGG_OcclusionQuery
{
	VkQueryPool queryPool = VK_NULL_HANDLE;
	mgbool isComplete = false;
	mgbool inBeginEndBlock = false;
	mgint pixelCount = 0;
	mgbool gpuHasBegun = false;
};

struct MGG_GraphicsSystem
{
	VkInstance instance;
	mgbool supportsPhysicalDeviceProperties2EXT;
	VkDebugUtilsMessengerEXT debugMessenger = VK_NULL_HANDLE;
	std::vector<MGG_GraphicsAdapter*> adapters;
};


static void MGVK_BufferCopyAndFlush(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int destOffset, mgbyte* data, int dataBytes);
static MGG_Buffer* MGVK_Buffer_Create(MGG_GraphicsDevice* device, MGBufferType type, mgint sizeInBytes, bool no_push);
static void MGVK_DestroyPipelines(MGG_GraphicsDevice* device, std::function<bool(const MGVK_PipelineState&)> compare);
static void MGVK_DestroyFrameResources(MGG_GraphicsDevice* device, FrameCounter currentFrame, mgbyte free_all);
static void MGVK_UpdateRenderPass(MGG_GraphicsDevice* device, FrameCounter currentFrame, VkCommandBuffer commandBuffer);
static VkCommandBuffer MGVK_BeginNewCommandBuffer(MGG_GraphicsDevice* device, VkCommandPool pool = VK_NULL_HANDLE);
static void MGVK_ExecuteAndFreeCommandBuffer(MGG_GraphicsDevice* device, VkCommandBuffer commandBuffer, VkCommandPool pool = VK_NULL_HANDLE);
static void MGVK_ProcessDescriptorCaches(MGG_GraphicsDevice* device, FrameCounter currentFrame);
static void MGVK_CmdTransitionImageLayout(
	VkCommandBuffer cmd,
	VkImage image,
	VkImageLayout oldLayout,
	VkImageLayout newLayout,
	VkImageAspectFlags aspectMask,
	uint32_t baseMipLevel = 0,
	uint32_t levelCount = 1,
	uint32_t baseArrayLayer = 0,
	uint32_t layerCount = 1);
static void MGVK_FlushCommands(MGG_GraphicsDevice* device, MGVK_Frame& frame);
static void MGVK_PrepareFrame(MGG_GraphicsDevice* device);
static void MGVK_CleanupPendingTransfers(MGG_GraphicsDevice* device);
static void MGVK_WaitPendingTransfers(MGG_GraphicsDevice* device, MGG_Texture* texture);
static void MGVK_RecordFrameCapture(MGG_GraphicsDevice* device, MGVK_Frame& frame, size_t slot);
static void MGVK_RecycleDiscardedBuffers(MGG_GraphicsDevice* device);
static void MGVK_CollectFrameCapture(MGG_GraphicsDevice* device, size_t slot);
static void MGVK_ReleaseCaptureSlot(MGG_GraphicsDevice* device, MGVK_CaptureSlot& slot);


static VkSampleCountFlagBits ToVkSampleCount(mgint multiSampleCount)
{
    switch (multiSampleCount)
    {
		case 0:
        case 1:
            return VK_SAMPLE_COUNT_1_BIT;
        case 2:
            return VK_SAMPLE_COUNT_2_BIT;
        case 4:
            return VK_SAMPLE_COUNT_4_BIT;
        case 8:
            return VK_SAMPLE_COUNT_8_BIT;
        case 16:
            return VK_SAMPLE_COUNT_16_BIT;
        case 32:
            return VK_SAMPLE_COUNT_32_BIT;
        case 64:
            return VK_SAMPLE_COUNT_64_BIT;
        default:
            assert(!"Unsupported sample count!");
            return VK_SAMPLE_COUNT_1_BIT;
    }
}

static VkFormat ToVkFormat(MGSurfaceFormat format)
{
	switch (format)
	{
	case MGSurfaceFormat::Color:
		return VK_FORMAT_R8G8B8A8_UNORM;
	case MGSurfaceFormat::Bgr565:
		return VK_FORMAT_B5G6R5_UNORM_PACK16;
	case MGSurfaceFormat::Bgra5551:
		return VK_FORMAT_B5G5R5A1_UNORM_PACK16;
	case MGSurfaceFormat::Bgra4444:
		return VK_FORMAT_A4R4G4B4_UNORM_PACK16;
	case MGSurfaceFormat::Dxt1:
		return VK_FORMAT_BC1_RGB_UNORM_BLOCK;
	case MGSurfaceFormat::Dxt3:
		return VK_FORMAT_BC2_UNORM_BLOCK;
	case MGSurfaceFormat::Dxt5:
		return VK_FORMAT_BC3_UNORM_BLOCK;
	case MGSurfaceFormat::NormalizedByte2:
		return VK_FORMAT_R8G8_UNORM;
	case MGSurfaceFormat::NormalizedByte4:
		return VK_FORMAT_R8G8B8A8_UNORM;
	case MGSurfaceFormat::Rgba1010102:
		return VK_FORMAT_A2R10G10B10_UNORM_PACK32;
	case MGSurfaceFormat::Rg32:
		return VK_FORMAT_R16G16_UNORM;
	case MGSurfaceFormat::Rgba64:
		return VK_FORMAT_R16G16B16A16_UNORM;
	case MGSurfaceFormat::Alpha8:
		return VK_FORMAT_R8_UNORM;
	case MGSurfaceFormat::Single:
		return VK_FORMAT_R32_SFLOAT;
	case MGSurfaceFormat::Vector2:
		return VK_FORMAT_R32G32_SFLOAT;
	case MGSurfaceFormat::Vector4:
		return VK_FORMAT_R32G32B32A32_SFLOAT;
	case MGSurfaceFormat::HalfSingle:
		return VK_FORMAT_R32G32B32A32_SFLOAT;
	case MGSurfaceFormat::HalfVector2:
		return VK_FORMAT_R16G16_SFLOAT;
	case MGSurfaceFormat::HalfVector4:
		return VK_FORMAT_R16G16B16A16_SFLOAT;
	case MGSurfaceFormat::HdrBlendable:
		return VK_FORMAT_R16G16B16A16_SFLOAT;
	case MGSurfaceFormat::Bgr32:
		return VK_FORMAT_B8G8R8A8_UNORM;
	case MGSurfaceFormat::Bgra32:
		return VK_FORMAT_B8G8R8A8_UNORM;
	case MGSurfaceFormat::ColorSRgb:
		return VK_FORMAT_R8G8B8A8_SRGB;
	case MGSurfaceFormat::Bgr32SRgb:
		return VK_FORMAT_B8G8R8A8_SRGB;
	case MGSurfaceFormat::Bgra32SRgb:
		return VK_FORMAT_B8G8R8A8_SRGB;
	case MGSurfaceFormat::Dxt1SRgb:
		return VK_FORMAT_BC1_RGB_SRGB_BLOCK;
	case MGSurfaceFormat::Dxt3SRgb:
		return VK_FORMAT_BC2_SRGB_BLOCK;
	case MGSurfaceFormat::Dxt5SRgb:
		return VK_FORMAT_BC3_SRGB_BLOCK;
	case MGSurfaceFormat::Dxt1a:
		return VK_FORMAT_BC1_RGBA_UNORM_BLOCK;
	case MGSurfaceFormat::Depth32Float:
		return VK_FORMAT_D32_SFLOAT;
	default:
		MGVK_UNREACHABLE();
	}
}

// Etervale fork: a render target whose image is its depth buffer (issue 016): bound as the depth attachment of a pass
// without colour, sampled through a depth view.
static bool MGVK_IsDepthSurface(const MGG_Texture* texture)
{
	return texture->format == MGSurfaceFormat::Depth32Float;
}

static VkFormat ToVkFormat(MGDepthFormat format)
{
	switch (format)
	{
	case MGDepthFormat::Depth16:
		return VkFormat::VK_FORMAT_D16_UNORM;
	case MGDepthFormat::Depth24:
	case MGDepthFormat::Depth24Stencil8:
#if defined(__APPLE__)
		return VkFormat::VK_FORMAT_D32_SFLOAT_S8_UINT;
#else
		return VkFormat::VK_FORMAT_D24_UNORM_S8_UINT;
#endif
	case MGDepthFormat::Depth32Float:
		return VkFormat::VK_FORMAT_D32_SFLOAT;
	default:
		return VkFormat::VK_FORMAT_UNDEFINED;
	}
}

static VkFormat ToVkFormat(MGVertexElementFormat format)
{
	switch (format)
	{
	case MGVertexElementFormat::Single:
		return VK_FORMAT_R32_SFLOAT;
	case MGVertexElementFormat::Vector2:
		return VK_FORMAT_R32G32_SFLOAT;
	case MGVertexElementFormat::Vector3:
		return VK_FORMAT_R32G32B32_SFLOAT;
	case MGVertexElementFormat::Vector4:
		return VK_FORMAT_R32G32B32A32_SFLOAT;
	case MGVertexElementFormat::Color:
		return VK_FORMAT_R8G8B8A8_UNORM;
	case MGVertexElementFormat::Byte4:
		return VK_FORMAT_R8G8B8A8_UINT;
	case MGVertexElementFormat::Short2:
		return VK_FORMAT_R16G16_SINT;
	case MGVertexElementFormat::Short4:
		return VK_FORMAT_R16G16B16A16_SINT;
	case MGVertexElementFormat::NormalizedShort2:
		return VK_FORMAT_R16G16_SNORM;
	case MGVertexElementFormat::NormalizedShort4:
		return VK_FORMAT_R16G16B16A16_SNORM;
	case MGVertexElementFormat::HalfVector2:
		return VK_FORMAT_R16G16_SFLOAT;
	case MGVertexElementFormat::HalfVector4:
		return VK_FORMAT_R16G16B16A16_SFLOAT;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkPrimitiveTopology ToVkPrimitiveTopology(MGPrimitiveType type)
{
	switch (type)
	{
	case MGPrimitiveType::TriangleList:
		return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
	case MGPrimitiveType::TriangleStrip:
		return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP;
	case MGPrimitiveType::LineList:
		return VK_PRIMITIVE_TOPOLOGY_LINE_LIST;
	case MGPrimitiveType::LineStrip:
		return VK_PRIMITIVE_TOPOLOGY_LINE_STRIP;
	case MGPrimitiveType::PointList:
		return VK_PRIMITIVE_TOPOLOGY_POINT_LIST;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkImageViewType ToVkImageViewType(MGTextureType type, int layerCount = 1)
{
	switch (type)
	{
	case MGTextureType::_2D:
		return layerCount > 1 ? VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK_IMAGE_VIEW_TYPE_2D;
	case MGTextureType::_3D:
		return VK_IMAGE_VIEW_TYPE_3D;
	case MGTextureType::Cube:
		return VK_IMAGE_VIEW_TYPE_CUBE;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkImageType ToVkImageType(MGTextureType type)
{
	switch (type)
	{
	case MGTextureType::_2D:
		return VK_IMAGE_TYPE_2D;
	case MGTextureType::_3D:
		return VK_IMAGE_TYPE_3D;
	case MGTextureType::Cube:
		return VK_IMAGE_TYPE_2D; // Cube maps are treated as 2D images with cube view.
	default:
		MGVK_UNREACHABLE();
	}
}

static VkImageCreateFlags ToVkImageCreateFlags(MGTextureType type)
{
	switch (type)
	{
	case MGTextureType::Cube:
		return VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT;
	}
	return 0;
}

static VkImageAspectFlags DetermineAspectMask(VkFormat format)
{
	VkImageAspectFlags result = (VkImageAspectFlags)0;
	switch (format) {
		// Depth
	case VK_FORMAT_D16_UNORM:
	case VK_FORMAT_X8_D24_UNORM_PACK32:
	case VK_FORMAT_D32_SFLOAT:
		result = VK_IMAGE_ASPECT_DEPTH_BIT;
		break;
		// Stencil
	case VK_FORMAT_S8_UINT:
		result = VK_IMAGE_ASPECT_STENCIL_BIT;
		break;
		// Depth/Stencil
	case VK_FORMAT_D16_UNORM_S8_UINT:
	case VK_FORMAT_D24_UNORM_S8_UINT:
	case VK_FORMAT_D32_SFLOAT_S8_UINT:
		result = VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT;
		break;
		// Undefined
	case VK_FORMAT_UNDEFINED:
		break;
		// Assume everything else is Color
	default:
		result = VK_IMAGE_ASPECT_COLOR_BIT;
		break;
	}
	return result;
}

bool AreValidationLayersSupported()
{
	uint32_t layerCount;
	vkEnumerateInstanceLayerProperties(&layerCount, nullptr);

	std::vector<VkLayerProperties> availableLayers(layerCount);
	vkEnumerateInstanceLayerProperties(&layerCount, availableLayers.data());

	for (const auto& layerProperties : availableLayers)
	{
		if (strcmp("VK_LAYER_KHRONOS_validation", layerProperties.layerName) == 0)
			return true;
	}

	return false;
}

static bool SupportsExtension(const std::vector<VkExtensionProperties>& supportedExtensions, const char* extensionName)
{
	for (const auto& extension : supportedExtensions)
	{
		if (strcmp(extension.extensionName, extensionName) == 0)
			return true;
	}

	return false;
}

MGG_GraphicsSystem* MGG_GraphicsSystem_Create()
{
#ifndef __APPLE__
	printf("Initializing volk.\n");
	auto err = volkInitialize();
	if (err != VK_SUCCESS)
	{
		printf("Failed to initialize volk!\n");
		return nullptr;
	}
#else
	auto err = VK_SUCCESS;
#endif

	VkApplicationInfo app_info = { VK_STRUCTURE_TYPE_APPLICATION_INFO };
	app_info.pNext = nullptr;
	app_info.apiVersion = VK_API_VERSION_1_0;

	// TODO: pass these thru from C# is more flexible!
	app_info.applicationVersion = VK_MAKE_VERSION(1, 0, 0);
	app_info.engineVersion = VK_MAKE_VERSION(1, 0, 0);
	app_info.pApplicationName = "Unknown";
	app_info.pEngineName = "MonoGame";

	std::vector<VkExtensionProperties> supportedInstanceExtensions;
	{
		uint32_t count;
		vkEnumerateInstanceExtensionProperties(nullptr, &count, nullptr);

		fflush(stdout);
		printf("Found %u Vulkan instance extensions.\n", count);
		fflush(stdout);

		supportedInstanceExtensions.resize(count);

		vkEnumerateInstanceExtensionProperties(nullptr, &count, supportedInstanceExtensions.data());

		fflush(stdout);
		printf("Supported Vulkan instance extensions:\n");
		for (const auto& ext : supportedInstanceExtensions)
		{
			printf("- %s\n", ext.extensionName);
		}
		fflush(stdout);
	}

	std::vector<const char*> instanceExtensions;
#if defined(MG_SDL2)
	{
		uint32_t count = 0;
		if (SDL_Vulkan_GetInstanceExtensions(nullptr, &count, nullptr))
		{
			fflush(stdout);
			printf("Found %u Vulkan instance extensions required by SDL.\n", count);
			fflush(stdout);

			instanceExtensions.resize(count);

			// This call returns the extensions that SDL needs for the created instance.
			if (SDL_Vulkan_GetInstanceExtensions(nullptr, &count, instanceExtensions.data()))
			{
				fflush(stdout);
				printf("Retrieved Vulkan instance extensions required by SDL:\n");
				for (const auto& ext : instanceExtensions)
				{
					printf("- %s\n", ext);
				}
				fflush(stdout);
			}
			else
			{
				fflush(stdout);
				printf("SDL_Vulkan_GetInstanceExtensions failed to populate: %s\n", SDL_GetError());
				fflush(stdout);

				return nullptr;
			}
		}
		else
		{
			fflush(stdout);
			printf("SDL_Vulkan_GetInstanceExtensions failed to get count: %s\n", SDL_GetError());
			fflush(stdout);

			return nullptr;
		}
	}
#endif

	// Check instance-level extensions support or if they are core in this instance
	uint32_t version;
	err = vkEnumerateInstanceVersion(&version);
	if (err != VK_SUCCESS)
	{
		printf("Failed to retrieve Vulkan instance version!\n");
		return nullptr;
	}

	printf("Vulkan instance version: %d.%d.%d\n", VK_API_VERSION_MAJOR(version), VK_API_VERSION_MINOR(version), VK_API_VERSION_PATCH(version));

	// This extension should be widely supported (~89% of systems according to https://vulkan.gpuinfo.org/listinstanceextensions.php?platform=all)
	// This extension is used to initialize the VK_EXT_custom_border_color (which therefore won't be supported if absent)
	bool supportsProperties2EXT = false;
	if (SupportsExtension(supportedInstanceExtensions, VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME))
	{
		supportsProperties2EXT = true;
		instanceExtensions.push_back(VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME);
	}
	else
	{
		printf("%s is not supported by this instance! VK_EXT_custom_border_color will not be supported either.\n", VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME);
	}

	//instanceExtensions.push_back(VK_GOOGLE_HLSL_FUNCTIONALITY_1_EXTENSION_NAME);

	std::vector<const char*> enabledLayers;

	if (MGVK_ValidationEnabled)
	{
		// This extension has a very poor support on Android (less than 25%). Chances are that DEBUG builds won't work on Android.
		if (SupportsExtension(supportedInstanceExtensions, VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
		{
			instanceExtensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
		}
		else
		{
			printf("%s is not supported by this instance. Labeling Vulkan object will not be possible.\n", VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
		}

		if (AreValidationLayersSupported())
		{
			enabledLayers.push_back("VK_LAYER_KHRONOS_validation");
			printf("Validation layers are enabled (performances will be drastically impacted). To run without validation layers, please use a RELEASE build or uninstall the Vulkan SDK.\n");
		}
		else
			printf("Validation layers aren't supported (you might want to install the Vulkan SDK to support them).\n");
	}

	VkInstanceCreateInfo instance_create_info = { VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO };
	instance_create_info.pApplicationInfo = &app_info;
	instance_create_info.enabledExtensionCount = instanceExtensions.size();
	instance_create_info.ppEnabledExtensionNames = instanceExtensions.data();
	instance_create_info.enabledLayerCount = enabledLayers.size();
	instance_create_info.ppEnabledLayerNames = enabledLayers.data();
	instance_create_info.pNext = nullptr;

	VkInstance instance = VK_NULL_HANDLE;

	err = vkCreateInstance(&instance_create_info, nullptr, &instance);
	if (err != VK_SUCCESS)
	{
		printf("Failed to create Vulkan instance!\n");
		return nullptr;
	}

#ifndef __APPLE__
	volkLoadInstance(instance);
#endif

	auto system = new MGG_GraphicsSystem();
	system->instance = instance;
	system->supportsPhysicalDeviceProperties2EXT = supportsProperties2EXT;

	// Gather the physical devices.
	{
		uint32_t count = 0;
		VkResult res = vkEnumeratePhysicalDevices(system->instance, &count, NULL);
		if (res == VK_SUCCESS)
		{
			std::vector<VkPhysicalDevice> gpus;
			gpus.resize(count);

			res = vkEnumeratePhysicalDevices(system->instance, &count, gpus.data());
			if (res == VK_SUCCESS)
			{
				for (const auto& gpu : gpus)
				{
					auto adapter = new MGG_GraphicsAdapter();
					adapter->device = gpu;

					vkGetPhysicalDeviceProperties(adapter->device, &adapter->properties);
					vkGetPhysicalDeviceFeatures(adapter->device, &adapter->features);
					vkGetPhysicalDeviceMemoryProperties(adapter->device, &adapter->memory);

					system->adapters.push_back(adapter);
				}
			}
		}
	}

	return system;
}

void MGG_GraphicsSystem_Destroy(MGG_GraphicsSystem* system)
{
	assert(system != nullptr);

    // Clean up all adapters
    for (auto adapter : system->adapters)
    {
        delete adapter;
    }
    system->adapters.clear();

    // Destroy the Vulkan instance
	if (system->instance)
	{
		vkDestroyInstance(system->instance, nullptr);
	}

	delete system;
}

MGG_GraphicsAdapter* MGG_GraphicsAdapter_Get(MGG_GraphicsSystem* system, mgint index)
{
	if (index < 0 || index >= system->adapters.size())
		return nullptr;

	return system->adapters[index];
}

void MGG_GraphicsAdapter_GetInfo(MGG_GraphicsAdapter* adapter, MGG_GraphicsAdaptor_Info& info)
{
	assert(adapter != nullptr);

	info.DeviceName = adapter->properties.deviceName;
	info.DeviceId = adapter->properties.deviceID;
	info.Revision = adapter->properties.driverVersion;
	info.VendorId = adapter->properties.vendorID;

	// TODO: Should we be generating a description?
	// Is there a description somewhere for us to get?
	static const char* description = "";
	info.Description = (void*)description;

	info.SubSystemId = 0;
	info.MonitorHandle = 0;

	// Get the number of display modes for the primary display
	int displayIndex = 0; // Primary display
	int numModes = SDL_GetNumDisplayModes(displayIndex);

	if (adapter->modes.size() == 0 && numModes > 0)
	{
		// Enumerate available display modes
		for (int i = 0; i < numModes; i++)
		{
			SDL_DisplayMode mode;
			if (SDL_GetDisplayMode(displayIndex, i, &mode) == 0)
			{
				MGG_DisplayMode displayMode;
				displayMode.width = mode.w;
				displayMode.height = mode.h;
				displayMode.format = MGSurfaceFormat::Color;

				bool found = false;
				for (auto m : adapter->modes)
				{
					if (m.width == displayMode.width &&
						m.height == displayMode.height)
					{
						found = true;
						break;
					}
				}

				if (!found)
					adapter->modes.push_back(displayMode);
			}
		}
	}

	// Get current display mode
	SDL_DisplayMode currentMode;
	if (SDL_GetCurrentDisplayMode(displayIndex, &currentMode) == 0)
	{
		info.CurrentDisplayMode.width = currentMode.w;
		info.CurrentDisplayMode.height = currentMode.h;
		info.CurrentDisplayMode.format = MGSurfaceFormat::Color;
	}
	else
	{
		// Fallback to desktop display mode
		SDL_DisplayMode desktopMode;
		if (SDL_GetDesktopDisplayMode(displayIndex, &desktopMode) == 0)
		{
			info.CurrentDisplayMode.width = desktopMode.w;
			info.CurrentDisplayMode.height = desktopMode.h;
			info.CurrentDisplayMode.format = MGSurfaceFormat::Color;
		}
		else
		{
			// Final fallback
			info.CurrentDisplayMode.width = 1920;
			info.CurrentDisplayMode.height = 1080;
			info.CurrentDisplayMode.format = MGSurfaceFormat::Color;
		}
	}
	info.DisplayModeCount = adapter->modes.size();
	info.DisplayModes = adapter->modes.data();
}

static void mggCreateImage(MGG_GraphicsDevice* device, VkImageCreateInfo* info, MGG_Texture* texture)
{
	VmaAllocationCreateInfo allocInfo = {};
	allocInfo.usage = VMA_MEMORY_USAGE_GPU_ONLY;
	VkResult res = vmaCreateImage(device->allocator, info, &allocInfo, &texture->image, &texture->allocation, nullptr);
	VK_CHECK_RESULT(res);
}

static MGG_Texture* CreateDepthTexture(MGG_GraphicsDevice* device, VkFormat format, uint32_t width, uint32_t height, mgint multiSampleCount)
{
	// TODO: Could convert this into a
	// general image creation method.

	MGG_Texture* texture = new MGG_Texture;
	memset(texture, 0, sizeof(MGG_Texture));
	texture->id = ++device->currentTextureId;

	VkImageCreateInfo& create_info = texture->info;
	create_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	create_info.imageType = VK_IMAGE_TYPE_2D;
	create_info.format = format;
	create_info.extent = { width, height, 1 };
	create_info.mipLevels = 1;
	create_info.arrayLayers = 1;
    create_info.samples = ToVkSampleCount(multiSampleCount);
	create_info.tiling = VK_IMAGE_TILING_OPTIMAL;
	create_info.usage = VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
	create_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
	create_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

	texture->multiSampleCount = multiSampleCount;
	texture->layouts[0] = VK_IMAGE_LAYOUT_UNDEFINED;
	texture->optimal_layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
	VkImageLayout optimalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    VkImageAspectFlags aspectMask = DetermineAspectMask(format);

	mggCreateImage(device, &create_info, texture);
    VK_SET_OBJECT_NAME(device->device, texture->image, VK_OBJECT_TYPE_IMAGE, "CreateDepthTexture::texture.image");

	{
		std::lock_guard lock(device->transferMutex);

		VkCommandBuffer cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
		MGVK_CmdTransitionImageLayout(
			cmd,
			texture->image,
			VK_IMAGE_LAYOUT_UNDEFINED,
			optimalLayout,
			aspectMask
		);

		MGVK_ExecuteAndFreeCommandBuffer(device, cmd, device->transferPool);
	}

	texture->layouts[0] = optimalLayout;
    texture->optimal_layout = optimalLayout;

	texture->writeFrame = -1;

	return texture;
}

static VkImageView CreateImageView(MGG_GraphicsDevice* device, MGG_Texture* texture, uint32_t level_count)
{
	VkFormat format = texture->info.format;
	uint32_t layer_count = texture->info.arrayLayers;

	VkImageAspectFlags aspect_mask = DetermineAspectMask(format);

	VkImageViewCreateInfo image_view_create_info = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
	if (texture->msImage && texture->multiSampleCount)
		image_view_create_info.image = texture->msImage;
	else
		image_view_create_info.image = texture->image;
	image_view_create_info.viewType = ToVkImageViewType(texture->type, layer_count);
	image_view_create_info.format = format;
	image_view_create_info.subresourceRange.aspectMask = aspect_mask;
	image_view_create_info.subresourceRange.baseMipLevel = 0;
	image_view_create_info.subresourceRange.levelCount = level_count;
	image_view_create_info.subresourceRange.baseArrayLayer = 0;
	image_view_create_info.subresourceRange.layerCount = layer_count;

	VkImageView view;
	VkResult res = vkCreateImageView(device->device, &image_view_create_info, NULL, &view);
	VK_CHECK_RESULT(res);

	return view;
}

static void MGVK_EndRenderPass(MGG_GraphicsDevice* device, VkCommandBuffer cmd_buffer)
{
    if (!device->inRenderPass)
        return;

    vkCmdEndRenderPass(cmd_buffer);
    device->inRenderPass = false;
}

static void MGVK_FlushCommands(MGG_GraphicsDevice* device, MGVK_Frame& frame)
{
	MGVK_EndRenderPass(device, frame.commandBuffer);

	VkResult res = vkEndCommandBuffer(frame.commandBuffer);
	VK_CHECK_RESULT(res);

	VkFence renderFence;
	{
		VkFenceCreateInfo fenceCreateInfo = {};
		fenceCreateInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
		fenceCreateInfo.flags = 0;
		vkCreateFence(device->device, &fenceCreateInfo, nullptr, &renderFence);
		VK_SET_OBJECT_NAME(device->device, renderFence, VK_OBJECT_TYPE_FENCE, "MGVK_FlushCommands.renderFence");
	}

	VkSubmitInfo submitInfo = {};
	submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
	submitInfo.commandBufferCount = 1;
	submitInfo.pCommandBuffers = &frame.commandBuffer;
	// Etervale fork: these commands may already draw into the frame's swapchain image (the UI before a read-back), which
	// is only usable once its acquisition has signalled: the first submission of the frame waits for it, and Present then
	// does not wait again. Upstream submitted without the wait, drawing into an image the presentation engine still owned.
	VkPipelineStageFlags acquireStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT;
	if (frame.image_index != (uint32_t)-1 && !frame.acquireWaited)
	{
		submitInfo.waitSemaphoreCount = 1;
		submitInfo.pWaitSemaphores = &frame.imageAcquiredSemaphore;
		submitInfo.pWaitDstStageMask = &acquireStage;
		frame.acquireWaited = true;
	}
	{
		std::lock_guard lock(device->queueMutex);
		vkQueueSubmit(device->queue, 1, &submitInfo, renderFence);
	}
	vkWaitForFences(device->device, 1, &renderFence, VK_TRUE, UINT64_MAX);
	vkDestroyFence(device->device, renderFence, nullptr);
}

static void MGVK_BufferCreate(MGG_GraphicsDevice* device, int sizeInBytes, VkBufferUsageFlags usage, VmaMemoryUsage flags, MGG_Buffer* buffer)
{
	VkBufferCreateInfo bufferInfo = { VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
	bufferInfo.size = sizeInBytes;
	bufferInfo.usage = usage;
	bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

	VmaAllocationCreateInfo allocInfo = {};
	allocInfo.usage = flags;

	VkResult res = vmaCreateBuffer(device->allocator, &bufferInfo, &allocInfo, &buffer->buffer, &buffer->allocation, nullptr);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, buffer->buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Buffer.buffer");
}

// Etervale fork (issue 006): a storage buffer is read and written by shaders, holds dispatch arguments, and is filled and
// read back by copies.
static const VkBufferUsageFlags MGVK_StorageBufferUsage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT |
	VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;

// Etervale fork (issues 005, 006): every stage and access that can touch a storage resource (or anything a dispatch
// wrote) after a copy or a dispatch: the barriers around them use these as the far side, so whichever pass comes next
// sees the writes.
static const VkPipelineStageFlags MGVK_StorageStages = VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_VERTEX_INPUT_BIT |
	VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
	VK_PIPELINE_STAGE_TRANSFER_BIT;
static const VkAccessFlags MGVK_StorageReadAccess = VK_ACCESS_INDIRECT_COMMAND_READ_BIT | VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT |
	VK_ACCESS_UNIFORM_READ_BIT | VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_READ_BIT |
	VK_ACCESS_TRANSFER_WRITE_BIT;

static VkBufferUsageFlags ToUsage(MGBufferType type)
{
	VkBufferUsageFlags usage;
	switch (type)
	{
	case MGBufferType::Index:
		return VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
		break;
	case MGBufferType::Vertex:
		return VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
		break;
	case MGBufferType::Constant:
		return VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
		break;
	case MGBufferType::Storage:
		return MGVK_StorageBufferUsage;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkCommandBuffer MGVK_BeginNewCommandBuffer(MGG_GraphicsDevice* device, VkCommandPool pool)
{
	VkCommandBufferAllocateInfo allocInfo = {};
	allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
	allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	allocInfo.commandPool = pool ? pool : device->cmdPool;
	allocInfo.commandBufferCount = 1;

	VkCommandBuffer commandBuffer;
	vkAllocateCommandBuffers(device->device, &allocInfo, &commandBuffer);
	VK_SET_OBJECT_NAME(device->device, commandBuffer, VK_OBJECT_TYPE_COMMAND_BUFFER, "MGVK_BeginNewCommandBuffer::commandBuffer");

	VkCommandBufferBeginInfo beginInfo = {};
	beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
	beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;

	vkBeginCommandBuffer(commandBuffer, &beginInfo);

	return commandBuffer;
}

static void MGVK_ExecuteAndFreeCommandBuffer(MGG_GraphicsDevice* device, VkCommandBuffer commandBuffer, VkCommandPool pool)
{
	vkEndCommandBuffer(commandBuffer);

	VkFence renderFence;
	{
		VkFenceCreateInfo fenceCreateInfo = {};
		fenceCreateInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
		fenceCreateInfo.flags = 0;
		vkCreateFence(device->device, &fenceCreateInfo, nullptr, &renderFence);
		VK_SET_OBJECT_NAME(device->device, renderFence, VK_OBJECT_TYPE_FENCE, "MGVK_ExecuteAndFreeCommandBuffer.renderFence");
	}

	{
		std::lock_guard lock(device->queueMutex);

		VkSubmitInfo submitInfo = {};
		submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
		submitInfo.commandBufferCount = 1;
		submitInfo.pCommandBuffers = &commandBuffer;

		vkQueueSubmit(device->queue, 1, &submitInfo, renderFence);
	}

	vkWaitForFences(device->device, 1, &renderFence, VK_TRUE, UINT64_MAX);
	vkDestroyFence(device->device, renderFence, nullptr);

	pool = pool ? pool : device->cmdPool;
	vkFreeCommandBuffers(device->device, pool, 1, &commandBuffer);
}

MGG_GraphicsDevice* MGG_GraphicsDevice_Create(MGG_GraphicsSystem* system, MGG_GraphicsAdapter* adapter)
{
	assert(system != nullptr);
	assert(adapter != nullptr);

	auto device = new MGG_GraphicsDevice();
	device->renderThread = std::this_thread::get_id(); // Etervale fork: in-frame texture uploads (MGG_Texture_SetData)

	device->instance = system->instance;
	device->physicalDevice = adapter->device;
	device->deviceFeatures = adapter->features;
	device->deviceProperties = adapter->properties;

	printf("Selected GPU: %s\n", device->deviceProperties.deviceName);
	printf("Supported Vulkan API version: %d.%d.%d\n", VK_API_VERSION_MAJOR(device->deviceProperties.apiVersion), VK_API_VERSION_MINOR(device->deviceProperties.apiVersion), VK_API_VERSION_PATCH(device->deviceProperties.apiVersion));

	// Capture some needed limits.
	device->minUniformBufferOffsetAlignment = adapter->properties.limits.minUniformBufferOffsetAlignment;

	uint32_t queueFamilyCount;
	vkGetPhysicalDeviceQueueFamilyProperties(device->physicalDevice, &queueFamilyCount, nullptr);
	assert(queueFamilyCount > 0);

	VkQueueFamilyProperties* queueFamilyProps = new VkQueueFamilyProperties[queueFamilyCount];
	device->graphicsQueueFamily = 0;
	{
		vkGetPhysicalDeviceQueueFamilyProperties(device->physicalDevice, &queueFamilyCount, queueFamilyProps);

		for (int i = 0; i < queueFamilyCount; i++)
		{
			if ((queueFamilyProps[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0)
			{
				device->graphicsQueueFamily = i;
				break;
			}
		}

		delete [] queueFamilyProps;
	}

	VkDeviceQueueCreateInfo queueCreateInfo {};
	queueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
	queueCreateInfo.queueFamilyIndex = device->graphicsQueueFamily;
	queueCreateInfo.queueCount = 1;
	float priority = 1.0f;
	queueCreateInfo.pQueuePriorities = &priority;

	// Check if VK_KHR_swapchain and VK_EXT_custom_border_color are supported
	std::vector<VkExtensionProperties> deviceExtensions;
	{
		uint32_t count;
		vkEnumerateDeviceExtensionProperties(device->physicalDevice, nullptr, &count, nullptr);
		deviceExtensions.resize(count);

		vkEnumerateDeviceExtensionProperties(device->physicalDevice, nullptr, &count, deviceExtensions.data());
	}

	bool swapChainSupported = false;
	bool scalarBlockLayoutSupported = false;
	bool hlslFunctionalitySupported = false;
	bool userTypeSupported = false;
	bool pipelineExecutableSupported = false;

	for (const auto& extension : deviceExtensions)
	{
		if (strcmp(extension.extensionName, VK_KHR_SWAPCHAIN_EXTENSION_NAME) == 0)
			swapChainSupported = true;
		if (strcmp(extension.extensionName, VK_EXT_SCALAR_BLOCK_LAYOUT_EXTENSION_NAME) == 0)
			scalarBlockLayoutSupported = true;
		if (strcmp(extension.extensionName, VK_EXT_CUSTOM_BORDER_COLOR_EXTENSION_NAME) == 0)
			device->customBorderColorSupported = system->supportsPhysicalDeviceProperties2EXT;
		if (strcmp(extension.extensionName, VK_GOOGLE_HLSL_FUNCTIONALITY1_EXTENSION_NAME) == 0)
			hlslFunctionalitySupported = true;
		if (strcmp(extension.extensionName, VK_GOOGLE_USER_TYPE_EXTENSION_NAME) == 0)
			userTypeSupported = true;
		if (strcmp(extension.extensionName, VK_KHR_PIPELINE_EXECUTABLE_PROPERTIES_EXTENSION_NAME) == 0)
			pipelineExecutableSupported = true;
	}

	if (!swapChainSupported)
	{
		printf("%s is not supported by this driver!\n", VK_KHR_SWAPCHAIN_EXTENSION_NAME);
		// This is a critical failure.
		return nullptr;
	}

	// This is technically required by -fvk-use-dx-layout, but the driver may still accept it and it'll just work. It might also cause shader compilation failures, or insane rendering.
	// This has pretty good coverage.
	if (!scalarBlockLayoutSupported)
		printf("%s is not supported by this driver!\n", VK_EXT_SCALAR_BLOCK_LAYOUT_EXTENSION_NAME);
	if (!device->customBorderColorSupported) // We can live without this extention.
		printf("%s is not supported by this driver!\n", VK_EXT_CUSTOM_BORDER_COLOR_EXTENSION_NAME);
	// This is only required for reflection information when compiling shaders, so worst case it's just a validation failure. TODO: Potentially look at stripping this from the SPIR-V?
	if (!hlslFunctionalitySupported)
		printf("%s is not supported by this driver!\n", VK_GOOGLE_HLSL_FUNCTIONALITY1_EXTENSION_NAME);
	// This is the same as above - fine if it's not there.
	if (!userTypeSupported)
		printf("%s is not supported by this driver!\n", VK_GOOGLE_USER_TYPE_EXTENSION_NAME);

	std::vector<const char*> extensions;
	extensions.push_back(VK_KHR_SWAPCHAIN_EXTENSION_NAME);

	VkPhysicalDeviceFeatures enabledFeatures = {};
	if (device->deviceFeatures.sampleRateShading)
	{
		enabledFeatures.sampleRateShading = VK_TRUE;
	}
	if (device->deviceFeatures.occlusionQueryPrecise)
	{
		enabledFeatures.occlusionQueryPrecise = VK_TRUE;
	}
	if (device->deviceFeatures.samplerAnisotropy)
	{
		enabledFeatures.samplerAnisotropy = VK_TRUE;
	}
	// Per-target blend states on MRT (BlendState.IndependentBlendEnable) need this feature.
	if (device->deviceFeatures.independentBlend)
	{
		enabledFeatures.independentBlend = VK_TRUE;
	}

	VkDeviceCreateInfo deviceCreateInfo = { VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO };
	deviceCreateInfo.queueCreateInfoCount = 1;
	deviceCreateInfo.pQueueCreateInfos = &queueCreateInfo;
	deviceCreateInfo.pEnabledFeatures = &enabledFeatures;

	void* lastFeature = nullptr;

	VkPhysicalDeviceCustomBorderColorFeaturesEXT customBorderColorFeatures = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_CUSTOM_BORDER_COLOR_FEATURES_EXT };
	if (device->customBorderColorSupported)
	{
		VkPhysicalDeviceCustomBorderColorFeaturesEXT supportedCbc = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_CUSTOM_BORDER_COLOR_FEATURES_EXT };
		VkPhysicalDeviceFeatures2 supportedFeatures2 = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 };
		supportedFeatures2.pNext = &supportedCbc;
		vkGetPhysicalDeviceFeatures2(device->physicalDevice, &supportedFeatures2);

		extensions.push_back(VK_EXT_CUSTOM_BORDER_COLOR_EXTENSION_NAME);
		customBorderColorFeatures.customBorderColors = supportedCbc.customBorderColors;
		customBorderColorFeatures.customBorderColorWithoutFormat = supportedCbc.customBorderColorWithoutFormat;
		customBorderColorFeatures.pNext = lastFeature;
		lastFeature = &customBorderColorFeatures;
	}
	VkPhysicalDeviceScalarBlockLayoutFeatures scalarFeatures = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES };
	if (scalarBlockLayoutSupported)
	{
		extensions.push_back(VK_EXT_SCALAR_BLOCK_LAYOUT_EXTENSION_NAME);
		scalarFeatures.scalarBlockLayout = VK_TRUE;
		scalarFeatures.pNext = lastFeature;
		lastFeature = &scalarFeatures;
	}
	if (hlslFunctionalitySupported)
		extensions.push_back(VK_GOOGLE_HLSL_FUNCTIONALITY1_EXTENSION_NAME);
	if (userTypeSupported)
		extensions.push_back(VK_GOOGLE_USER_TYPE_EXTENSION_NAME);

	// Etervale fork (issue 012): the drivers' per-pipeline statistics (registers, instructions, spills...), on request:
	// capturing them may slow pipeline creation, so a game turns them on for a measurement run only.
	VkPhysicalDevicePipelineExecutablePropertiesFeaturesKHR executableFeatures = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_EXECUTABLE_PROPERTIES_FEATURES_KHR };
	const char* statisticsRequest = getenv("MG_PIPELINE_STATISTICS");
	if (statisticsRequest != nullptr && strcmp(statisticsRequest, "1") == 0)
	{
		if (pipelineExecutableSupported)
		{
			extensions.push_back(VK_KHR_PIPELINE_EXECUTABLE_PROPERTIES_EXTENSION_NAME);
			executableFeatures.pipelineExecutableInfo = VK_TRUE;
			executableFeatures.pNext = lastFeature;
			lastFeature = &executableFeatures;
			device->pipelineStatistics = true;
		}
		else
			printf("%s is not supported by this driver: no pipeline statistics.\n", VK_KHR_PIPELINE_EXECUTABLE_PROPERTIES_EXTENSION_NAME);
	}

	VkPhysicalDeviceFeatures2 deviceFeatures2 = { VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2 };
	if (lastFeature != nullptr)
	{
		deviceFeatures2.features = enabledFeatures;
		deviceFeatures2.pNext = lastFeature;

		deviceCreateInfo.pEnabledFeatures = nullptr;
		deviceCreateInfo.pNext = &deviceFeatures2;
	}

	deviceCreateInfo.enabledExtensionCount = extensions.size();
	deviceCreateInfo.ppEnabledExtensionNames = extensions.data();

	auto res = vkCreateDevice(device->physicalDevice, &deviceCreateInfo, NULL, &device->device);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, device->device, VK_OBJECT_TYPE_DEVICE, "MGG_GraphicsDevice.device");
	VK_SET_OBJECT_NAME(device->device, device->physicalDevice, VK_OBJECT_TYPE_PHYSICAL_DEVICE, "MGG_GraphicsDevice.physicalDevice");

	VmaAllocatorCreateInfo allocatorInfo = {};
	allocatorInfo.instance = system->instance;
	allocatorInfo.physicalDevice = device->physicalDevice;
	allocatorInfo.device = device->device;
	vmaCreateAllocator(&allocatorInfo, &device->allocator);

	vkGetDeviceQueue(device->device, device->graphicsQueueFamily, 0, &device->queue);

	VkCommandPoolCreateInfo cmdPoolInfo = { VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO };
	cmdPoolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
	cmdPoolInfo.queueFamilyIndex = device->graphicsQueueFamily;
	res = vkCreateCommandPool(device->device, &cmdPoolInfo, nullptr, &device->cmdPool);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, device->cmdPool, VK_OBJECT_TYPE_COMMAND_POOL, "MGG_GraphicsDevice.cmdPool");

	// Create the command pool for graphics transfers.
	res = vkCreateCommandPool(device->device, &cmdPoolInfo, nullptr, &device->transferPool);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, device->transferPool, VK_OBJECT_TYPE_COMMAND_POOL, "MGG_GraphicsDevice.transferPool");

	// Create the pipeline cache which is used at runtime
	// to speed up pipeline creation.
	VkPipelineCacheCreateInfo pipelineCache{ VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO };
	pipelineCache.pNext = NULL;
	pipelineCache.initialDataSize = 0;
	pipelineCache.pInitialData = NULL;
	pipelineCache.flags = 0;
	res = vkCreatePipelineCache(device->device, &pipelineCache, nullptr, &device->pipelineCache);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, device->pipelineCache, VK_OBJECT_TYPE_PIPELINE_CACHE, "MGG_GraphicsDevice.pipelineCache");

	// Initialize all the device state.
	device->scissorDirty = true;
	device->uniformsDirty = 0xFFFFFFFF;
	device->textureSamplerDirty = 0xFFFFFFFF;
	device->vertexBuffersDirty = 0xFFFFFFFF;
	device->pipelineStateDirty = true;
	device->currentTextureId = 1;
	device->currentSamplerId = 1;
	device->blendFactorDirty = true;
	device->renderTargetDirty = true;
	device->inRenderPass = false;
	device->blendFactor[0] = device->blendFactor[1] = device->blendFactor[2] = device->blendFactor[3] = 1;
	memset(device->descriptorSets, 0, sizeof(device->descriptorSets));
	memset(device->dynamicOffsets, 0, sizeof(device->dynamicOffsets));
	memset(device->vertexBuffers, 0, sizeof(device->vertexBuffers));
	memset(device->vertexOffsets, 0, sizeof(device->vertexOffsets));
	memset(device->uniforms, 0, sizeof(device->uniforms));
	memset(device->textures, 0, sizeof(device->textures));
	memset(device->samplers, 0, sizeof(device->samplers));
	memset(&device->pipelineState, 0, sizeof(device->pipelineState));

	// Setup default "null" textures for each type.
	{
		auto tex = MGG_Texture_Create(device, MGTextureType::_2D, MGSurfaceFormat::Color, 2, 2, 1, 1, 1);
		uint32_t black[] = {0,0,0,0};
		MGG_Texture_SetData(device, tex, 0, 0, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		device->nullTexture[(int)MGTextureType::_2D] = tex;
	}
	{
		auto tex = MGG_Texture_Create(device, MGTextureType::_3D, MGSurfaceFormat::Color, 2, 2, 2, 1, 1);
		uint32_t black[] = {0,0,0,0,0,0,0,0};
		MGG_Texture_SetData(device, tex, 0, 0, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		device->nullTexture[(int)MGTextureType::_3D] = tex;
	}
	{
		auto tex = MGG_Texture_Create(device, MGTextureType::Cube, MGSurfaceFormat::Color, 2, 2, 1, 1, 6);
		uint32_t black[] = {0,0,0,0};
		MGG_Texture_SetData(device, tex, 0, 0, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		MGG_Texture_SetData(device, tex, 0, 1, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		MGG_Texture_SetData(device, tex, 0, 2, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		MGG_Texture_SetData(device, tex, 0, 3, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		MGG_Texture_SetData(device, tex, 0, 4, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		MGG_Texture_SetData(device, tex, 0, 5, 0, 0, 0, 0, 0, 0, (mgbyte*)black, sizeof(black));
		device->nullTexture[(int)MGTextureType::Cube] = tex;
	}

	// Etervale fork: what an empty read-only storage buffer slot reads (issue 006).
	device->nullStorageBuffer = MGVK_Buffer_Create(device, MGBufferType::Storage, 16, true);

	return device;
}

static void MGVK_CleanupSwapChain(MGG_GraphicsDevice* device)
{
	{
		std::lock_guard lock(device->queueMutex);
		vkQueueWaitIdle(device->queue);
	}

	// Destroy all the frame resources.
	device->pipelineState.targets = nullptr;

	// Destroy framebuffers and render passes that are dependent on the swapchain.
	// We iterate this way because we are removing elements from the map.
	auto& cache = device->targetCache;
	for (auto itr = cache.begin(); itr != cache.end();)
	{
		bool usesSwapchain = false;
		auto targetSetCache = itr->second;

		// Check if any of the targets in this cache entry are part of the swapchain.
		for (int i = 0; i < targetSetCache->set.numTargets; ++i)
		{
			if (targetSetCache->set.targets[i] && targetSetCache->set.targets[i]->isSwapchain)
			{
				usesSwapchain = true;
				break;
			}
		}

		if (usesSwapchain)
		{
			// Clean up any pipelines that point at this target set cache.
			MGVK_DestroyPipelines(device, [targetSetCache](const MGVK_PipelineState& s)
				{
					return s.targets == targetSetCache;
				});

			// This cache entry uses the swapchain, so it's safe to destroy.
			for (int i = 0; i < MGVK_NUM_TARGETS; ++i)
			{
				auto& view = targetSetCache->arraySlicesViews[i];
				if (view.has_value()) {
					vkDestroyImageView(device->device, view.value(), nullptr);
				}
			}
			vkDestroyRenderPass(device->device, targetSetCache->renderPass, nullptr);
			vkDestroyFramebuffer(device->device, targetSetCache->framebuffer, nullptr);
			delete targetSetCache;

			// Erase the element and get an iterator to the next one.
			itr = cache.erase(itr);
		}
		else
		{
			// This is an off-screen render target, so leave it alone and
			// move to the next element.
			++itr;
		}
	}

	if (device->swapchain != VK_NULL_HANDLE)
	{
		vkDestroySwapchainKHR(device->device, device->swapchain, nullptr);
		device->swapchain = VK_NULL_HANDLE;
	}

	// Cleanup the swap chain images.
	for (size_t i = 0; i < device->swapchainCount; i++)
	{
		auto& swap = device->swapchains[i];
		auto chain = swap.texture;
		swap.texture = nullptr;

		if (chain == nullptr)
			continue;

		// Note the image is freed by vkDestroySwapchainKHR.
		vkDestroyImageView(device->device, chain->target_view, nullptr);
		vmaFreeMemory(device->allocator, chain->allocation);

		// Cleanup MSAA resolve image.
		if (chain->msImage != nullptr)
		{
			vkDestroyImageView(device->device, chain->resolve_view, nullptr);
			vmaDestroyImage(device->allocator, chain->msImage, chain->msAllocation);
		}

		if (chain->depthTexture != nullptr)
		{
			vkDestroyImageView(device->device, chain->depthTexture->target_view, nullptr);
			vmaDestroyImage(device->allocator, chain->depthTexture->image, chain->depthTexture->allocation);
			delete chain->depthTexture;
		}

		delete chain;
	}
}

void MGG_GraphicsDevice_Destroy(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);

	// Be sure we're done drawing.
	// Prevents some exceptions while shutting down.
	vkDeviceWaitIdle(device->device);

	MGVK_WaitPendingTransfers(device, nullptr);

	MGVK_CleanupPendingTransfers(device);

	// Etervale fork: staging buffers of in-frame uploads and the frame capture's copies.
	for (auto& staged : device->frameStaging)
		vmaDestroyBuffer(device->allocator, staged.second.buffer, staged.second.allocation);
	device->frameStaging.clear();
	for (auto& slot : device->captureSlots)
		MGVK_ReleaseCaptureSlot(device, slot);
	device->captureSlots.clear();

	MGVK_CleanupSwapChain(device);

	for (auto pair : device->shader_programs)
	{
		vkDestroyPipelineLayout(device->device, pair.second->layout, nullptr);
		delete pair.second;
	}
	device->shader_programs.clear();

	vkDestroyPipelineCache(device->device, device->pipelineCache, nullptr);

	while (device->all_buffers.size() > 0)
		MGG_Buffer_Destroy(device, device->all_buffers[0]);

	for (size_t i = 0; i < device->swapchainCount; i++)
	{
		auto& frame = device->frames[i];

		vkDestroySemaphore(device->device, frame.imageAcquiredSemaphore, nullptr);
		vkDestroyFence(device->device, frame.completedFence, nullptr);
		vkFreeCommandBuffers(device->device, device->cmdPool, 1, &frame.commandBuffer);
	}

	for (size_t i = 0; i < device->swapchainCount; i++)
	{
		auto& swap = device->swapchains[i];
		vkDestroySemaphore(device->device, swap.renderCompleteSemaphore, nullptr);
	}

	vkDestroyCommandPool(device->device, device->transferPool, nullptr);
	vkDestroyCommandPool(device->device, device->cmdPool, nullptr);

	for (int i=0; i < 3; i++)
		MGG_Texture_Destroy(device, device->nullTexture[i]);

	for (size_t i = 0; i < device->swapchainCount; i++)
		MGVK_DestroyFrameResources(device, i, true);

	if (device->surface != nullptr)
		vkDestroySurfaceKHR(device->instance, device->surface, nullptr);

	vmaDestroyAllocator(device->allocator);

	vkDestroyDevice(device->device, nullptr);

	delete device;
}

void MGG_GraphicsDevice_GetCaps(MGG_GraphicsDevice* device, MGG_GraphicsDevice_Caps& caps)
{
	assert(device != nullptr);

	// TODO: Get actual stats from the device!

	caps.MaxTextureSlots = 16;
	caps.MaxVertexBufferSlots = 8;
	caps.MaxVertexTextureSlots = 8;

	// TODO: The following values match what were hard coded on the managed
	// 		 side in GraphicsCapabilities.Native.cs
	//
	//		 Need to update the hard code values to get the actual caps
	//		 from the device itself.
	caps.MaxTextureAnisotropy = 16;
	caps.SupportsNonPowerOfTwo = true;
	caps.SupportsTextureFilterAnisotropic = true;
	caps.MaxMultiSampleCount = 4;
	caps.SupportsDepth24 = true;
	caps.SupportsPackedDepthStencil = true;
	caps.SupportsDepthNonLinear = false;
	caps.SupportsTextureMaxLevel = true;
	caps.SupportsDxt1 = true;
	caps.SupportsS3tc = true;
	caps.SupportsSRgb = true;
	caps.SupportsDepthClamp = true;
	caps.SupportsTextureArrays = true;
	caps.SupportsVertexTextures = true;
	caps.SupportsFloatTextures = true;
	caps.SupportsHalfFloatTextures = true;
	caps.SupportsNormalized = true;
	caps.SupportsInstancing = true;
	caps.SupportsBaseIndexInstancing = true;
	caps.SupportsSeparateBlendStates = true;

	// Vulkan shader profile from pipeline.
	caps.ShaderProfile = 80;
}

mgint MGG_GraphicsDevice_GetBackBufferMultiSampleCount(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);
	return device->multiSampleCount;
}

void MGVK_RecreateSwapChain(
	MGG_GraphicsDevice* device,
	void* nativeWindowHandle,
	mguint width,
	mguint height,
	VkFormat vkColor,
	VkFormat vkDepth,
	mgint multiSampleCount,
	mgint syncInterval)
{
	assert(device != nullptr);
	assert(nativeWindowHandle != nullptr);

	vkDeviceWaitIdle(device->device);

	std::lock_guard lock(device->queueMutex);

	VkResult res;

	// Create the surface.
#if defined(MG_SDL2)
	auto sdl_window = (SDL_Window*)nativeWindowHandle;
	if (sdl_window != device->window)
	{
		MGVK_CleanupSwapChain(device);

		if (device->surface != nullptr)
			vkDestroySurfaceKHR(device->instance, device->surface, nullptr);

		if (!SDL_Vulkan_CreateSurface(sdl_window, device->instance, &device->surface))
		{
			printf("SDL_Vulkan_CreateSurface failed: %s\n", SDL_GetError());
			fflush(stdout);

			device->surface = VK_NULL_HANDLE;
			return;
		}
		VK_SET_OBJECT_NAME(device->device, (uint64_t)device->surface, VK_OBJECT_TYPE_SURFACE_KHR, "MGG_GraphicsDevice.surface");

		device->window = sdl_window;
	}
#else
#error Not Implemented
#endif

	MGVK_CleanupSwapChain(device);

	VkSurfaceCapabilitiesKHR surface_capabilities;
	res = vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device->physicalDevice, device->surface, &surface_capabilities);
	VK_CHECK_RESULT(res);

	// If max extent is zero'd, it means the window is minimized, and we should leave the swapchain to VK_NULL_HANDLE and stop rendering (this is done in MGP_Platform_BeforeDraw()).
	if (surface_capabilities.maxImageExtent.width == 0 || surface_capabilities.maxImageExtent.height == 0)
		return;

#ifdef DEBUG
	printf("RecreateSwapchain: req=%d,%d, cur=%d,%d, min=%d,%d, max=%d,%d\n",
		width, height,
		surface_capabilities.currentExtent.width, surface_capabilities.currentExtent.height,
		surface_capabilities.minImageExtent.width, surface_capabilities.minImageExtent.height,
		surface_capabilities.maxImageExtent.width, surface_capabilities.maxImageExtent.height);
#endif

	// Clamp the multisample count to the max supported.
	{
		mgint maxMultisampleCount = 1;
		VkSampleCountFlags counts = device->deviceProperties.limits.framebufferColorSampleCounts;
		if ((counts & VK_SAMPLE_COUNT_64_BIT) != 0)
			maxMultisampleCount = 64;
		else if ((counts & VK_SAMPLE_COUNT_32_BIT) != 0)
			maxMultisampleCount = 32;
		else if ((counts & VK_SAMPLE_COUNT_16_BIT) != 0)
			maxMultisampleCount = 16;
		else if ((counts & VK_SAMPLE_COUNT_8_BIT) != 0)
			maxMultisampleCount = 8;
		else if ((counts & VK_SAMPLE_COUNT_4_BIT) != 0)
			maxMultisampleCount = 4;
		else if ((counts & VK_SAMPLE_COUNT_2_BIT) != 0)
			maxMultisampleCount = 2;

		multiSampleCount = std::clamp(multiSampleCount, 1, maxMultisampleCount);
	}

	// We store the requested/intended size as canonical swapchain dimensions on the device.
	device->swapchainWidth = width;
	device->swapchainHeight = height;

	// Clamp for actual swapchain creation.
	// We apply the extent range to the entire swapchain size to avoid surface scaling and errors.
	uint32_t clampedWidth = std::clamp(width, surface_capabilities.minImageExtent.width, surface_capabilities.maxImageExtent.width);
	uint32_t clampedHeight = std::clamp(height, surface_capabilities.minImageExtent.height, surface_capabilities.maxImageExtent.height);
	device->colorFormat = vkColor;
	device->depthFormat = vkDepth;
	device->multiSampleCount = multiSampleCount;

	// Check if the requested color format is supported, and fallback to another one otherwise.
	VkFormat surface_format = VK_FORMAT_UNDEFINED;
	{
		uint32_t format_count = 0;
		res = vkGetPhysicalDeviceSurfaceFormatsKHR(device->physicalDevice, device->surface, &format_count, nullptr);
		VK_CHECK_RESULT(res);

		std::vector<VkSurfaceFormatKHR> surfFormats(format_count);
		res = vkGetPhysicalDeviceSurfaceFormatsKHR(device->physicalDevice, device->surface, &format_count, surfFormats.data());
		VK_CHECK_RESULT(res);

	RETRY_SURFACE_FORMAT_SEARCH:

		for (const auto& surfFormat : surfFormats)
		{
			if (surfFormat.colorSpace != VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
				continue;
			if (surfFormat.format != vkColor)
				continue;

			// The expected format is supported.
			surface_format = surfFormat.format;
			break;
		}

		// Some hardware only supports BGRA.
		// For the swapchain allow BGRA to match to RGBA.
		if (surface_format == VK_FORMAT_UNDEFINED && vkColor == VK_FORMAT_R8G8B8A8_UNORM)
		{
			vkColor = VK_FORMAT_B8G8R8A8_UNORM;
			goto RETRY_SURFACE_FORMAT_SEARCH;
		}

		if (surface_format == VK_FORMAT_UNDEFINED && vkColor == VK_FORMAT_R8G8B8A8_SRGB)
		{
			vkColor = VK_FORMAT_B8G8R8A8_SRGB;
			goto RETRY_SURFACE_FORMAT_SEARCH;
		}

		if (surface_format == VK_FORMAT_UNDEFINED)
		{
			// TODO: We need a better "log" method that isn't just printfs.
			// TODO: Would be nice to log the unsupported surface format.

			printf("Requested swapchain format was unsupported!\n");
			return;
		}
	}

	// Requested swapchain extent will be clamped based on the surface's min/max extent.
	// In most cases these will match the current extent, as specified at window/surface creation time.
	VkExtent2D extent;
	extent.width = clampedWidth;
	extent.height = clampedHeight;

	/*
	VkSwapchainPresentScalingCreateInfoEXT scalingCreateInfo = {};
	scalingCreateInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_SCALING_CREATE_INFO_EXT;
	scalingCreateInfo.scalingBehavior = VK_PRESENT_SCALING_ASPECT_RATIO_STRETCH_BIT_EXT | VK_PRESENT_SCALING_STRETCH_BIT_EXT;
	scalingCreateInfo.presentGravityX = VK_PRESENT_GRAVITY_CENTERED_BIT_EXT;
	scalingCreateInfo.presentGravityY = VK_PRESENT_GRAVITY_CENTERED_BIT_EXT;
	*/

	// Query supported present modes and select the best one
	uint32_t presentModeCount = 0;
	res = vkGetPhysicalDeviceSurfacePresentModesKHR(device->physicalDevice, device->surface, &presentModeCount, nullptr);
	VK_CHECK_RESULT(res);

	std::vector<VkPresentModeKHR> presentModes(presentModeCount);
	res = vkGetPhysicalDeviceSurfacePresentModesKHR(device->physicalDevice, device->surface, &presentModeCount, presentModes.data());
	VK_CHECK_RESULT(res);

	// Prefer MAILBOX -> IMMEDIATE -> FIFO (FIFO is always supported)
	device->syncInterval = syncInterval; // 0 is IMMEDIATE, 1 is either MAILBOX or FIFO, 2 is half-Vsync and we currently don't support that on Vulkan.
	VkPresentModeKHR selectedPresentMode = VK_PRESENT_MODE_FIFO_KHR; // Default, guaranteed to be supported by Vulkan specs.
	for (const auto& presentMode : presentModes)
	{
		// We used to upgrade FIFO to MAILBOX when supported because it should be preferred,
		// but driver support seems to be broken sometimes. Some drivers report MAILBOX
		// as supported but will actually behave like IMMEDIATE instead.
		/*
		if (device->syncInterval > 0 &&
			presentModes[i] == VK_PRESENT_MODE_MAILBOX_KHR)
		{
			selectedPresentMode = VK_PRESENT_MODE_MAILBOX_KHR;
			fprintf(stderr, "Vsync is upgraded.\n");
			break;
		}
		*/
		// Vsync is disabled, set IMMEDIATE if supported
		if (device->syncInterval == 0 &&
			presentMode == VK_PRESENT_MODE_IMMEDIATE_KHR)
		{
			selectedPresentMode = VK_PRESENT_MODE_IMMEDIATE_KHR;
			break;
		}
	}

	VkSwapchainCreateInfoKHR create_info = { VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR };
	create_info.surface = device->surface;
	create_info.imageFormat = surface_format;
	create_info.imageColorSpace = VK_COLOR_SPACE_SRGB_NONLINEAR_KHR;
	create_info.imageExtent = extent;
	create_info.imageArrayLayers = 1;
	create_info.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
	create_info.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
	create_info.preTransform = surface_capabilities.currentTransform;
	create_info.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
	create_info.presentMode = selectedPresentMode;
	create_info.clipped = VK_TRUE;
	//create_info.pNext = &scalingCreateInfo;

	// This seems to just be a suggestion to vkCreateSwapchainKHR and
	// vkGetSwapchainImagesKHR will really define the final number.
	create_info.minImageCount = std::max(2u, surface_capabilities.minImageCount);
	if (surface_capabilities.maxImageCount > 0)
		create_info.minImageCount = std::min(create_info.minImageCount, surface_capabilities.maxImageCount);

	res = vkCreateSwapchainKHR(device->device, &create_info, nullptr, &device->swapchain);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, device->swapchain, VK_OBJECT_TYPE_SWAPCHAIN_KHR, "MGG_GraphicsDevice.swapchain");

	uint32_t swapchainCount = 0;
	res = vkGetSwapchainImagesKHR(device->device, device->swapchain, &swapchainCount, NULL);
	VK_CHECK_RESULT(res);
	printf("Swapchain: %u images of %ux%u, present mode %d\n", swapchainCount, extent.width, extent.height, (int)selectedPresentMode);

	// Do we need to change the number of swapchain images?  This can happen for various reasons
	// including display changes and refresh rates.
	if (swapchainCount != device->swapchainCount)
	{
		// Free any existing swapchain frame info and recreate it.
		if (device->swapchains.size() > 0)
		{
			for (size_t i = 0; i < device->swapchainCount; i++)
			{
				// TODO: Should we wait for fences here or is vkDeviceWaitIdle enough?

				auto& frame = device->frames[i];
				vkDestroySemaphore(device->device, frame.imageAcquiredSemaphore, nullptr);
				vkDestroyFence(device->device, frame.completedFence, nullptr);
				if (frame.uniforms)
					MGG_Buffer_Destroy(device, frame.uniforms);
				MGVK_DestroyFrameResources(device, i, true);

				auto& swap = device->swapchains[i];
				vkDestroySemaphore(device->device, swap.renderCompleteSemaphore, nullptr);
			}

			device->frames.clear();
			device->swapchains.clear();
		}

		// Since we know that all rendering has stopped it is
		// safe to free all the descriptors and let them recreate
		// on the next draw.  This removes all flicker caused by
		// bad descriptor caches.
		MGVK_ProcessDescriptorCaches(device, 0);

		device->swapchainCount = swapchainCount;
		device->freeFrames = device->swapchainCount + 1;

		VkCommandBufferAllocateInfo comBufferInfo =
		{
			VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
			NULL,
			device->cmdPool,
			VK_COMMAND_BUFFER_LEVEL_PRIMARY,
			1
		};

		device->frames.resize(swapchainCount);
		device->swapchains.resize(swapchainCount);

		for (int i = 0; i < device->swapchainCount; i++)
		{
			auto& frame = device->frames[i];

			res = vkAllocateCommandBuffers(device->device, &comBufferInfo, &frame.commandBuffer);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, frame.commandBuffer, VK_OBJECT_TYPE_COMMAND_BUFFER, "MGVK_Frame.buffer[%d]", i);

			VkSemaphoreCreateInfo semaphore_create_info = { VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO };
			res = vkCreateSemaphore(device->device, &semaphore_create_info, NULL, &frame.imageAcquiredSemaphore);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, frame.imageAcquiredSemaphore, VK_OBJECT_TYPE_SEMAPHORE, "MGVK_Frame.imageAcquiredSemaphore");

			VkFenceCreateInfo fence_create_info = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
			fence_create_info.flags = 0;//VK_FENCE_CREATE_SIGNALED_BIT;
			res = vkCreateFence(device->device, &fence_create_info, NULL, &frame.completedFence);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, frame.completedFence, VK_OBJECT_TYPE_FENCE, "MGVK_Frame.completedFence[%d]", i);

			auto& swap = device->swapchains[i];
			res = vkCreateSemaphore(device->device, &semaphore_create_info, NULL, &swap.renderCompleteSemaphore);
			VK_CHECK_RESULT(res);
		}
	}

	std::vector<VkImage> swapchainImages(swapchainCount);
	res = vkGetSwapchainImagesKHR(device->device, device->swapchain, &swapchainCount, swapchainImages.data());
	VK_CHECK_RESULT(res);

	for (uint32_t i = 0; i < swapchainCount; ++i)
	{
		MGG_Texture* texture = new MGG_Texture;
		memset(texture, 0, sizeof(MGG_Texture));

		texture->writeFrame = -1;

		VkImageCreateInfo image_create_info = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
		image_create_info.imageType = VK_IMAGE_TYPE_2D;
		image_create_info.format = surface_format;
		image_create_info.extent = { clampedWidth, clampedHeight, 1 };
		image_create_info.mipLevels = 1;
		image_create_info.arrayLayers = 1;
		image_create_info.samples = VK_SAMPLE_COUNT_1_BIT;
		image_create_info.tiling = VK_IMAGE_TILING_OPTIMAL;
		image_create_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
		image_create_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
		image_create_info.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

		texture->info = image_create_info;
		texture->image = swapchainImages[i];
		texture->isSwapchain = texture->isTarget = true;
		texture->multiSampleCount = multiSampleCount;
		VK_SET_OBJECT_NAME(device->device, texture->image, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.image (Swapchain %d)", i);

		if (multiSampleCount > 1)
		{
			image_create_info.samples = ToVkSampleCount(multiSampleCount);
			image_create_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

			VmaAllocationCreateInfo allocInfo = {};
			allocInfo.usage = VMA_MEMORY_USAGE_GPU_ONLY;
			VkResult res = vmaCreateImage(device->allocator, &image_create_info, &allocInfo, &texture->msImage, &texture->msAllocation, nullptr);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, texture->msImage, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.msImage (Swapchain %d)", i);

			{
				VkImageViewCreateInfo image_view_create_info = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
				image_view_create_info.image = texture->image;
				image_view_create_info.viewType = ToVkImageViewType(texture->type);
				image_view_create_info.format = texture->info.format;
				image_view_create_info.subresourceRange.aspectMask = DetermineAspectMask(texture->info.format);
				image_view_create_info.subresourceRange.baseMipLevel = 0;
				image_view_create_info.subresourceRange.levelCount = 1;
				image_view_create_info.subresourceRange.baseArrayLayer = 0;
				image_view_create_info.subresourceRange.layerCount = texture->info.arrayLayers;

				VkResult res = vkCreateImageView(device->device, &image_view_create_info, NULL, &texture->resolve_view);
				VK_CHECK_RESULT(res);
			}
		}

		texture->target_view = CreateImageView(device, texture, image_create_info.mipLevels);
		VK_SET_OBJECT_NAME(device->device, texture->target_view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.target_view (Swapchain %d)", i);

		if (device->depthFormat != VK_FORMAT_UNDEFINED)
		{
            texture->depthTexture = CreateDepthTexture(device, device->depthFormat, texture->info.extent.width, texture->info.extent.height, texture->multiSampleCount);
			VK_SET_OBJECT_NAME(device->device, texture->depthTexture->image, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.depthTexture.image (Swapchain %d)", i);
			texture->depthTexture->target_view = CreateImageView(device, texture->depthTexture, 1);
			VK_SET_OBJECT_NAME(device->device, texture->depthTexture->target_view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.depthTexture.target_view (Swapchain %d)", i);
		}

		texture->layouts[0] = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
		device->swapchains[i].texture = texture;
	}


	for (int i = 0; i < device->swapchainCount; i++)
	{
		auto& frame = device->frames[i];
		frame.image_index = -1;
		vkDestroySemaphore(device->device, frame.imageAcquiredSemaphore, nullptr);
		VkSemaphoreCreateInfo semaphore_create_info = { VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO };
		res = vkCreateSemaphore(device->device, &semaphore_create_info, NULL, &frame.imageAcquiredSemaphore);
		VK_CHECK_RESULT(res);
		VK_SET_OBJECT_NAME(device->device, frame.imageAcquiredSemaphore, VK_OBJECT_TYPE_SEMAPHORE, "MGVK_Frame.imageAcquiredSemaphore");
	}
}

void MGVK_RecreateSwapChain(MGG_GraphicsDevice* device)
{
	MGVK_CleanupSwapChain(device);

	if (device->window == nullptr)
	{
		printf("Cannot recreate swapchain: window is null!\n");
		fflush(stdout);

		return;
	}

#ifdef MG_SDL2
	// Ensure any pending resize is flushed in the SDL event queue.
	SDL_PumpEvents();
#endif

	// Preserve the developer's intended backbuffer dimensions.
	MGVK_RecreateSwapChain(
		device,
		device->window,
		device->swapchainWidth,
		device->swapchainHeight,
		device->colorFormat,
		device->depthFormat,
		device->multiSampleCount,
		device->syncInterval);

    MGG_GraphicsDevice_SetRenderTargets(device, nullptr, nullptr, 0);
}

mgbyte MGG_GraphicsDevice_ResizeSwapchain(
	MGG_GraphicsDevice* device,
	void* nativeWindowHandle,
	mgint width,
	mgint height,
	MGSurfaceFormat color,
	MGDepthFormat depth,
	mgint multiSampleCount,
	mgint syncInterval)
{
	assert(device);

	// There is no zero... always at least 1.
	if (multiSampleCount == 0)
		multiSampleCount = 1;

	auto vkColor = ToVkFormat(color);
	auto vkDepth = ToVkFormat(depth);

	// Skip recreation if nothing of importance has actually changed.
	if (device->swapchain != VK_NULL_HANDLE &&
		device->swapchainWidth == static_cast<uint32_t>(width) &&
		device->swapchainHeight == static_cast<uint32_t>(height) &&
		device->colorFormat == vkColor &&
		device->depthFormat == vkDepth &&
		device->syncInterval == syncInterval &&
		device->multiSampleCount == multiSampleCount)
	{
		return 1;
	}

	bool isSwapchainBound = device->targets.targets[0] != nullptr &&
		device->targets.targets[0]->isSwapchain;

	// Etervale fork: the window manager resizes the window in the middle of a frame (a window taller than the work area).
	// The frame's command buffer was then begun again by MGVK_PrepareFrame while a render pass was open in it and the
	// current target set had been destroyed with the swapchain: the next Clear found no render pass to begin
	// (inRenderPass still set) and dereferenced a null target set (crash in vkCmdClearAttachments). The commands recorded
	// so far are submitted first (waiting on the swapchain image they may draw to), and the next draw begins a new render
	// pass for the targets still bound.
	if (device->swapchain != VK_NULL_HANDLE && device->frameIndex < device->frames.size() && device->frames[device->frameIndex].is_recording)
	{
		auto& frame = device->frames[device->frameIndex];
		MGVK_EndRenderPass(device, frame.commandBuffer);
		VkResult res = vkEndCommandBuffer(frame.commandBuffer);
		VK_CHECK_RESULT(res);

		VkFence fence;
		VkFenceCreateInfo fenceInfo = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
		vkCreateFence(device->device, &fenceInfo, nullptr, &fence);
		VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
		VkSubmitInfo submitInfo = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
		submitInfo.waitSemaphoreCount = frame.image_index != (uint32_t)-1 && !frame.acquireWaited ? 1 : 0;
		frame.acquireWaited = frame.acquireWaited || frame.image_index != (uint32_t)-1;
		submitInfo.pWaitSemaphores = &frame.imageAcquiredSemaphore;
		submitInfo.pWaitDstStageMask = &waitStage;
		submitInfo.commandBufferCount = 1;
		submitInfo.pCommandBuffers = &frame.commandBuffer;
		{
			std::lock_guard lock(device->queueMutex);
			vkQueueSubmit(device->queue, 1, &submitInfo, fence);
		}
		vkWaitForFences(device->device, 1, &fence, VK_TRUE, UINT64_MAX);
		vkDestroyFence(device->device, fence, nullptr);
		frame.is_recording = false;
	}
	device->renderTargetDirty = true;
	device->pipelineStateDirty = true;

	MGVK_RecreateSwapChain(device, nativeWindowHandle, width, height, vkColor, vkDepth, multiSampleCount, syncInterval);

	MGVK_PrepareFrame(device);

	if (isSwapchainBound)
	{
		MGG_GraphicsDevice_SetRenderTargets(device, nullptr, nullptr, 0);
	}

	return 1;
}


static void MGVK_ProcessDescriptorCaches(MGG_GraphicsDevice* device, FrameCounter currentFrame)
{
	// Here we go thru the shaders and move any
	// descriptors that haven't been used in a few
	// frames to the correct free list.

	// TODO: This could be a ton of iteration.
	// Maybe keep a list of recently used shaders
	// or descriptors and only process those?

	auto shader = device->all_shaders.begin();
	for (; shader != device->all_shaders.end(); shader++)
	{
		auto& usedSets = (*shader)->usedSets;
		auto& freeSets = (*shader)->freeSets;

		auto pair = usedSets.begin();
		for (; pair != usedSets.end();)
		{
			auto diff = currentFrame - pair->second->frame;
			if (diff < device->freeFrames)
			{
				pair++;
				continue;
			}

			freeSets.push(pair->second);
			pair = usedSets.erase(pair);
		}
	}
}

void MGVK_BeginCommandBuffer(VkCommandBuffer commandBuffer)
{
	VkCommandBufferBeginInfo beginInfo =
	{
		VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
		NULL,
		VK_COMMAND_BUFFER_USAGE_SIMULTANEOUS_USE_BIT,
		NULL
	};

	auto res = vkResetCommandBuffer(commandBuffer, VK_COMMAND_BUFFER_RESET_RELEASE_RESOURCES_BIT);
	VK_CHECK_RESULT(res);
	res = vkBeginCommandBuffer(commandBuffer, &beginInfo);
	VK_CHECK_RESULT(res);

	//vkCmdSetDepthClampEnableEXT(commandBuffer, VK_TRUE);
}

void MGVK_TryAcquireSwap(MGG_GraphicsDevice* device, MGVK_Frame& frame)
{
	VkResult res;

	// Block and wait for the frame to be done rendering.
	if (frame.is_rendering)
	{
		res = vkWaitForFences(device->device, 1, &frame.completedFence, VK_TRUE, UINT64_MAX);
		VK_CHECK_RESULT(res);
		frame.is_rendering = false;
		MGVK_CollectFrameCapture(device, device->frameIndex);
	}

	// Do we need to get the next swapchain image?
	if (device->swapchain != VK_NULL_HANDLE && frame.image_index == -1)
	{
		res = vkAcquireNextImageKHR(device->device, device->swapchain, UINT64_MAX,
			frame.imageAcquiredSemaphore, VK_NULL_HANDLE, &frame.image_index);
		VK_CHECK_RESULT(res);
		frame.acquireWaited = false;
	}

	// This should be cleared by now.
	res = vkResetFences(device->device, 1, &frame.completedFence);
	VK_CHECK_RESULT(res);
}

void MGVK_PrepareFrame(MGG_GraphicsDevice* device)
{
	auto& frame = device->frames[device->frameIndex];

	// This is only here for the first frame or for after the
	// swapchain is resized...  normally this occurs on Present.
	MGVK_TryAcquireSwap(device, frame);

	// Cleanup resources from the last time this frame was rendered.
	MGVK_ProcessDescriptorCaches(device, device->frame);
	MGVK_RecycleDiscardedBuffers(device);

	// Etervale fork: staging buffers of in-frame texture uploads, once their frame is complete.
	for (size_t i = 0; i < device->frameStaging.size();)
	{
		auto& staged = device->frameStaging[i];
		if (staged.first + device->swapchainCount > device->frame)
		{
			i++;
			continue;
		}
		vmaDestroyBuffer(device->allocator, staged.second.buffer, staged.second.allocation);
		staged = device->frameStaging.back();
		device->frameStaging.pop_back();
	}

	// MGVK_DestroyFrameResources uses currentFrame and device->freeFrames to calculate age.
	// We should pass the device->frame rather than swapchain_image_index.
	// Otherwise age calculation will underflow.
	MGVK_DestroyFrameResources(device, device->frame, false);

	frame.uniformOffset = 0;
	if (frame.uniforms == NULL)
	{
		frame.uniforms = MGVK_Buffer_Create(device, MGBufferType::Constant, 32 * 1024 * 1024, true);
		VK_SET_OBJECT_NAME(device->device, frame.uniforms->buffer, VK_OBJECT_TYPE_BUFFER, "MGVK_FrameState.uniforms->buffer");
	}

	MGVK_BeginCommandBuffer(frame.commandBuffer);

	//device->dynamicOffsets[0] = 0;
	//device->dynamicOffsets[1] = 0;

	frame.is_recording = true;
}

mgint MGG_GraphicsDevice_BeginFrame(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);

	// We do nothing here... everything is handled in Present().
	return device->frameIndex;
}

void MGG_GraphicsDevice_Clear(MGG_GraphicsDevice* device, MGClearOptions options, Vector4& color, mgfloat depth, mgint stencil)
{
	assert(device != nullptr);

	// Make sure something was set to be cleared.
	if ((mgint)options == 0)
		return;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	MGVK_UpdateRenderPass(device, device->frame, frame.commandBuffer);

	int num_attachments = 0;

	VkClearAttachment attachments[5];
	memset(attachments, 0, sizeof(attachments));

	auto targets = device->pipelineState.targets;

	// Etervale fork: a depth surface (issue 016) has no colour to clear; its depth is the target's own image.
	const bool depthSurface = MGVK_IsDepthSurface(targets->set.targets[0]);
	if (((int)options & (int)MGClearOptions::Target) != 0 && !depthSurface)
	{
		for (int i = 0; i < targets->set.numTargets; i++)
		{
			VkClearAttachment* attachment = &attachments[num_attachments];
			attachment->aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
			attachment->colorAttachment = i;
			attachment->clearValue.color.float32[0] = color.X;
			attachment->clearValue.color.float32[1] = color.Y;
			attachment->clearValue.color.float32[2] = color.Z;
			attachment->clearValue.color.float32[3] = color.W;
			num_attachments++;
		}
	}

	bool clearDepth = ((int)options & (int)MGClearOptions::DepthBuffer) != 0;
	bool clearStencil = ((int)options & (int)MGClearOptions::Stencil) != 0;
	if (clearDepth || clearStencil)
	{
		VkClearAttachment* attachment = &attachments[num_attachments];

		if (clearDepth)
			attachment->aspectMask |= VK_IMAGE_ASPECT_DEPTH_BIT;
		if (clearStencil)
			attachment->aspectMask |= VK_IMAGE_ASPECT_STENCIL_BIT;

		auto depthTexture = depthSurface ? targets->set.targets[0] : targets->set.targets[0]->depthTexture;
		if (depthTexture) {
			attachment->aspectMask &= DetermineAspectMask(depthTexture->info.format);
		}
		else {
			attachment->aspectMask = 0;
		}

		if (attachment->aspectMask != 0)
		{
			attachment->clearValue.depthStencil.depth = depth;
			attachment->clearValue.depthStencil.stencil = stencil;
			num_attachments++;
		}
	}

	if (num_attachments == 0)
		return;

	VkClearRect rect;
	rect.rect.offset.x = 0;
	rect.rect.offset.y = 0;
	rect.baseArrayLayer = 0;
	rect.layerCount = 1;

	// Clear always clears the entire target.
	rect.rect.extent.width = targets->width;
	rect.rect.extent.height = targets->height;

	vkCmdClearAttachments(frame.commandBuffer, num_attachments, attachments, 1, &rect);
}

static void MGVK_DestroyTargetSets(MGG_GraphicsDevice* device, std::function<bool(const MGVK_TargetSetCache*)> compare)
{
	auto& cache = device->targetCache;
	auto itr = cache.cbegin();

	while (itr != cache.cend())
	{
		if (!compare(itr->second))
			itr++;
		else
		{
            for (int i = 0; i < MGVK_NUM_TARGETS; ++i) {
                auto& view = itr->second->arraySlicesViews[i];
                if (view.has_value()) {
                    vkDestroyImageView(device->device, view.value(), nullptr);
                }
            }
			vkDestroyFramebuffer(device->device, itr->second->framebuffer, nullptr);
			vkDestroyRenderPass(device->device, itr->second->renderPass, nullptr);
			delete itr->second;
			cache.erase(itr++);
		}
	}

	device->pipelineState.targets = nullptr;
}

size_t MGVK_PipelineKeyHash::operator()(const MGVK_PipelineKey& key) const
{
	return MG_ComputeHash((const mgbyte*)&key, sizeof(MGVK_PipelineKey));
}

static MGVK_PipelineKey MGVK_MakePipelineKey(const MGVK_PipelineState& state)
{
	MGVK_PipelineKey key;
	key.program = state.program ? state.program->uid : 0;
	key.layout = state.layout ? state.layout->uid : 0;
	key.targets = state.targets ? state.targets->uid : 0;
	key.blend = state.blendState ? state.blendState->uid : 0;
	key.rasterizer = state.rasterizerState ? state.rasterizerState->uid : 0;
	key.depthStencil = state.depthStencilState ? state.depthStencilState->uid : 0;
	key.topology = (uint32_t)state.topology;
	return key;
}

// The cached state created from exactly these bytes, or null (Etervale fork: the hash alone returned a different state
// on a collision; two everyday blend states collide, e.g. DestinationColor/Zero against Zero/InverseSourceAlpha).
template <typename State>
static State* MGVK_FindState(std::unordered_multimap<uint32_t, State*>& cache, uint32_t hash, const void* source, size_t size)
{
	auto range = cache.equal_range(hash);
	for (auto itr = range.first; itr != range.second; ++itr)
	{
		if (memcmp(&itr->second->source, source, size) == 0)
			return itr->second;
	}
	return nullptr;
}

template <typename State>
static void MGVK_ForgetState(std::unordered_multimap<uint32_t, State*>& cache, State* state)
{
	auto range = cache.equal_range(state->hash);
	for (auto itr = range.first; itr != range.second; ++itr)
	{
		if (itr->second == state)
		{
			cache.erase(itr);
			return;
		}
	}
}

// Target sets compare by their defined content: an unengaged std::optional's storage is indeterminate.
static bool MGVK_SameTargetSet(const MGVK_TargetSet& a, const MGVK_TargetSet& b)
{
	if (a.numTargets != b.numTargets)
		return false;
	for (int i = 0; i < MGVK_NUM_TARGETS; i++)
	{
		if (a.targets[i] != b.targets[i] || a.firstUse[i] != b.firstUse[i] || a.arraySlices[i] != b.arraySlices[i])
			return false;
	}
	return true;
}

static uint32_t MGVK_HashTargetSet(const MGVK_TargetSet& set)
{
	uint32_t hash = MG_ComputeHash((const mgbyte*)set.targets, sizeof(set.targets));
	hash = MG_ComputeHash((const mgbyte*)set.firstUse, sizeof(set.firstUse), hash);
	hash = MG_ComputeHash((mguint)set.numTargets, hash);
	for (int i = 0; i < MGVK_NUM_TARGETS; i++)
		hash = MG_ComputeHash((mguint)set.arraySlices[i].value_or(-1), hash);
	return hash;
}

static void MGVK_DestroyPipelines(MGG_GraphicsDevice* device, std::function<bool(const MGVK_PipelineState&)> compare)
{
	auto& pipelines = device->pipelines;
	auto itr = pipelines.cbegin();

	while (itr != pipelines.cend())
	{
		if (!compare(itr->second.state))
			itr++;
		else
		{
			vkDestroyPipeline(device->device, itr->second.cache, nullptr);
			pipelines.erase(itr++);
		}
	}
}

static void MGVK_DestroyFrameResources(MGG_GraphicsDevice* device, FrameCounter currentFrame, mgbyte free_all)
{
	assert(device != nullptr);

	// Delete resources that haven't been used in a few frames
	{
		while (!device->destroyBuffers.empty())
		{
			auto buffer = device->destroyBuffers.front();
			auto diff = currentFrame - buffer->frame;
			if (!free_all && diff < device->freeFrames)
				break;

			device->destroyBuffers.pop();
			vmaDestroyBuffer(device->allocator, buffer->buffer, buffer->allocation);
			delete buffer;
		}

		while (!device->destroyTextures.empty())
		{
			auto texture = device->destroyTextures.front();
			auto diff = currentFrame - texture->frame;
			if (!free_all && diff < device->freeFrames)
				break;

			device->destroyTextures.pop();

			{
				std::lock_guard lock(device->transferMutex);
				MGVK_WaitPendingTransfers(device, texture);
			}

			if (texture->isTarget)
			{
				MGVK_DestroyPipelines(device, [texture](const MGVK_PipelineState& s)
				{
					// Just being defensive. s.targets should'nt be null.
					// I want to make sure swap-chain cleanup won't have an impact here.
					if (!s.targets)
					{
						return false;
					}

					return	s.targets->set.targets[0] == texture ||
						s.targets->set.targets[1] == texture ||
						s.targets->set.targets[2] == texture ||
						s.targets->set.targets[3] == texture;
				});

				MGVK_DestroyTargetSets(device, [texture](const MGVK_TargetSetCache* s)
				{
					return	s->set.targets[0] == texture ||
						s->set.targets[1] == texture ||
						s->set.targets[2] == texture ||
						s->set.targets[3] == texture;
				});
			}

			if (texture->target_view != VK_NULL_HANDLE)
				vkDestroyImageView(device->device, texture->target_view, nullptr);
			if (texture->view != VK_NULL_HANDLE)
				vkDestroyImageView(device->device, texture->view, nullptr);
			for (auto view : texture->storage_views)	// Etervale fork: the views of a storage image's mips
			{
				if (view != VK_NULL_HANDLE)
					vkDestroyImageView(device->device, view, nullptr);
			}

			if (texture->msImage != VK_NULL_HANDLE)
			{
				vkDestroyImageView(device->device, texture->resolve_view, nullptr);
				vmaDestroyImage(device->allocator, texture->msImage, texture->msAllocation);
			}

			if (texture->allocation != VK_NULL_HANDLE)
			{
				vmaDestroyImage(device->allocator, texture->image, texture->allocation);
			}
			mg_remove(device->all_textures, texture);
			delete texture;
		}

		while (device->destroyBlendStates.size() > 0)
		{
			auto state = device->destroyBlendStates.front();
			auto diff = currentFrame - state->frame;
			if (!free_all && diff < device->freeFrames)
				break;

			device->destroyBlendStates.pop();

			MGVK_DestroyPipelines(device, [state](const MGVK_PipelineState& s) { return s.blendState == state; });
			// Etervale fork: the device must not keep pointing at freed memory (the next state can be allocated at the
			// same address). MonoGame applies a state again before the next draw of a frame.
			if (device->pipelineState.blendState == state)
			{
				device->pipelineState.blendState = nullptr;
				device->pipelineStateDirty = true;
			}
			delete state;
		}

		while (device->destroyRasterizerStates.size() > 0)
		{
			auto state = device->destroyRasterizerStates.front();
			auto diff = currentFrame - state->frame;
			if (!free_all && diff < device->freeFrames)
				break;

			device->destroyRasterizerStates.pop();

			MGVK_DestroyPipelines(device, [state](const MGVK_PipelineState& s) { return s.rasterizerState == state; });
			// Etervale fork: the device must not keep pointing at freed memory (the next state can be allocated at the
			// same address). MonoGame applies a state again before the next draw of a frame.
			if (device->pipelineState.rasterizerState == state)
			{
				device->pipelineState.rasterizerState = nullptr;
				device->pipelineStateDirty = true;
			}
			delete state;
		}

		while (device->destroyDepthStencilStates.size() > 0)
		{
			auto state = device->destroyDepthStencilStates.front();
			auto diff = currentFrame - state->frame;
			if (!free_all && diff < device->freeFrames)
				break;

			device->destroyDepthStencilStates.pop();

			MGVK_DestroyPipelines(device, [state](const MGVK_PipelineState& s) { return s.depthStencilState == state; });
			// Etervale fork: the device must not keep pointing at freed memory (the next state can be allocated at the
			// same address). MonoGame applies a state again before the next draw of a frame.
			if (device->pipelineState.depthStencilState == state)
			{
				device->pipelineState.depthStencilState = nullptr;
				device->pipelineStateDirty = true;
			}
			delete state;
		}

		while (device->destroySamplers.size() > 0)
		{
			auto sampler = device->destroySamplers.front();
			device->destroySamplers.pop();
			vkDestroySampler(device->device, sampler->sampler, nullptr);
			delete sampler;
		}
	}
}

void MGVK_WaitPendingTransfers(MGG_GraphicsDevice* device, MGG_Texture* texture)
{
	auto& transfers = device->pendingTransfers;

	for (size_t i = 0; i < transfers.size();)
	{
		auto& pending = transfers[i];
		if (texture != nullptr && pending.owner != texture)
		{
			i++;
			continue;
		}

		vkWaitForFences(device->device, 1, &pending.done, VK_TRUE, UINT64_MAX);

		vkFreeCommandBuffers(device->device, device->transferPool, 1, &pending.cmd);
		vmaDestroyBuffer(device->allocator, pending.staging.buffer, pending.staging.allocation);
		vkDestroyFence(device->device, pending.done, nullptr);

		transfers[i] = transfers.back();
		transfers.pop_back();
	}

}

void MGVK_CleanupPendingTransfers(MGG_GraphicsDevice* device)
{
	bool reset = false;
	auto& transfers = device->pendingTransfers;
	for (size_t i = 0; i < transfers.size(); )
	{
		auto& pending = transfers[i];
		VkResult res = vkGetFenceStatus(device->device, pending.done);
		if (res == VK_NOT_READY)
		{
			i++;
			continue;
		}

		vkFreeCommandBuffers(device->device, device->transferPool, 1, &pending.cmd);
		vmaDestroyBuffer(device->allocator, pending.staging.buffer, pending.staging.allocation);
		vkDestroyFence(device->device, pending.done, nullptr);

		transfers[i] = transfers.back();
		transfers.pop_back();
		reset = true;
	}

	if (reset && transfers.size() == 0)
	{
		vkResetCommandPool(device->device, device->transferPool, VK_COMMAND_POOL_RESET_RELEASE_RESOURCES_BIT);
	}
}

void MGG_GraphicsDevice_Present(MGG_GraphicsDevice* device, mgint currentFrame, mgint syncInterval)
{
	assert(device != nullptr);
	assert(syncInterval >= 0);
	assert(currentFrame >= 0);
	assert((device->frame % device->swapchainCount) == currentFrame);


	auto frameIndex = currentFrame % device->swapchainCount;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);
	auto& swap = device->swapchains[frame.image_index];

	MGVK_EndRenderPass(device, frame.commandBuffer);
	MGVK_RecordFrameCapture(device, frame, device->frameIndex);

	VkResult res;

	{
		std::lock_guard lock(device->queueMutex);

		res = vkEndCommandBuffer(frame.commandBuffer);
		VK_CHECK_RESULT(res);
		frame.is_recording = false;

		VkSemaphore waits[] = { frame.imageAcquiredSemaphore };
		VkPipelineStageFlags waitFlags[] = { VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT };

		VkSubmitInfo submitInfo = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
		submitInfo.pNext = nullptr;
		submitInfo.waitSemaphoreCount = frame.acquireWaited ? 0 : 1;
		submitInfo.pWaitSemaphores = waits;
		submitInfo.pWaitDstStageMask = waitFlags;
		submitInfo.commandBufferCount = 1;
		submitInfo.pCommandBuffers = &frame.commandBuffer;
		submitInfo.signalSemaphoreCount = 1;
		submitInfo.pSignalSemaphores = &swap.renderCompleteSemaphore;
		res = vkQueueSubmit(device->queue, 1, &submitInfo, frame.completedFence);

		VkPresentInfoKHR presentInfo = { VK_STRUCTURE_TYPE_PRESENT_INFO_KHR };
		presentInfo.waitSemaphoreCount = 1;
		presentInfo.pWaitSemaphores = &swap.renderCompleteSemaphore;
		presentInfo.swapchainCount = 1;
		presentInfo.pSwapchains = &device->swapchain;
		presentInfo.pImageIndices = &frame.image_index;
		res = vkQueuePresentKHR(device->queue, &presentInfo);
	}

	// Cleanup any finished transfers as they may be
	// done after the block waiting for the present.
	{
		std::lock_guard lock(device->transferMutex);
		MGVK_CleanupPendingTransfers(device);
	}

	if (res == VK_ERROR_OUT_OF_DATE_KHR)
	{
		// This will happen if the window is minimized too.
		printf("Swapchain out of date. Recreating swapchain...\n");

		MGVK_RecreateSwapChain(device);

		if (device->swapchain == VK_NULL_HANDLE)
			printf("Couldn't recreate swapchain!\n");
	}
	else if (res == VK_SUBOPTIMAL_KHR)
	{
		// Intentionally ignored. The swapchain is still presentable.
		// This fires when the swapchain size differs from the window size.
		// macOS or Wayland compositors perform scaling, so this is not an error.
	}
	else
	{
		VK_CHECK_RESULT(res);
	}

	frame.image_index = -1;
	frame.is_rendering = true;

	// Etervale fork: buffers discarded this frame wait in the pending list until no frame in flight can read them
	// (MGVK_RecycleDiscardedBuffers). Upstream moved them to the free list one Present later, while with three or more
	// swapchain images the frame that discarded them could still be on the GPU: a later frame's SetData overwrote vertices
	// it was drawing (UI quads and light sprites missing or misplaced for a frame).
	while (device->discarded != nullptr)
	{
		auto buffer = device->discarded;
		device->discarded = buffer->next;
		buffer->next = device->pending;
		device->pending = buffer;
	}

	{
		std::lock_guard lock(device->queueMutex);

		// Increment to the next frame.
		++device->frame;
		device->frameIndex = device->frame % device->swapchainCount;

		// Get the next swap frame here so that any blocking
		// waiting for the GPU to finish occurs during Present.
		MGVK_PrepareFrame(device);
	}
}

void MGG_GraphicsDevice_SetBlendState(MGG_GraphicsDevice* device, MGG_BlendState* state, mgfloat factorR, mgfloat factorG, mgfloat factorB, mgfloat factorA)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (device->pipelineState.blendState != state)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.blendState = state;
	}

	if (device->blendFactor[0] != factorR ||
		device->blendFactor[1] != factorG ||
		device->blendFactor[2] != factorB ||
		device->blendFactor[3] != factorA)
	{
		device->blendFactor[0] = factorR;
		device->blendFactor[1] = factorG;
		device->blendFactor[2] = factorB;
		device->blendFactor[3] = factorA;
		device->blendFactorDirty = true;
	}
}

void MGG_GraphicsDevice_SetDepthStencilState(MGG_GraphicsDevice* device, MGG_DepthStencilState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (device->pipelineState.depthStencilState != state)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.depthStencilState = state;
	}
}

void MGG_GraphicsDevice_SetRasterizerState(MGG_GraphicsDevice* device, MGG_RasterizerState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (device->pipelineState.rasterizerState != state)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.rasterizerState = state;
	}
}

void MGG_GraphicsDevice_GetTitleSafeArea(mgint& x, mgint& y, mgint& width, mgint& height)
{
	// Nothing for PC here unless we want to support
	// things like Steam TV modes and we need platform
	// specific calls for that.
}

void MGG_GraphicsDevice_SetViewport(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height, mgfloat minDepth, mgfloat maxDepth)
{
	assert(device != nullptr);

	VkViewport& viewport = device->viewport;
	viewport.x = x;
	viewport.y = y;
	viewport.width = width;
	viewport.height = height;
	viewport.minDepth = minDepth;
	viewport.maxDepth = maxDepth;
	device->viewportSet = true;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	vkCmdSetViewport(frame.commandBuffer, 0, 1, &viewport);
}

void MGG_GraphicsDevice_SetScissorRectangle(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height)
{
	assert(device != nullptr);

	VkRect2D& scissor = device->scissor;
	scissor.offset.x = x;
	scissor.offset.y = y;
	scissor.extent.width = width;
	scissor.extent.height = height;
	device->scissorDirty = true;
	device->scissorSet = true;
}

void MGG_GraphicsDevice_SetRenderTargets(MGG_GraphicsDevice* device, MGG_Texture** targets, mgint* arraySlices, mgint count)
{
	assert(device != nullptr);

	auto& frame = device->frames[device->frameIndex];

	if (targets == nullptr || count == 0)
	{
		if (frame.image_index == -1)
			return;

		device->targets.targets[0] = device->swapchains[frame.image_index].texture;
		memset(device->targets.targets + 1, 0, sizeof(MGG_Texture*) * (MGVK_NUM_TARGETS - 1));

		device->targets.numTargets = 1;
        for (int i = 0; i < MGVK_NUM_TARGETS; i++)
            device->targets.arraySlices[i] = std::nullopt;
	}
	else
	{
        for (int i = 0; i < MGVK_NUM_TARGETS; i++)
        {
            if (i < count)
            {
				device->targets.targets[i] = targets[i];
				if (arraySlices && arraySlices[i] >= 0)
					device->targets.arraySlices[i] = arraySlices[i];
            }
            else
            {
				device->targets.targets[i] = nullptr;
                device->targets.arraySlices[i] = std::nullopt;
            }
        }

		device->targets.numTargets = count;
	}

	device->pipelineStateDirty = true;
	device->renderTargetDirty = true;
	device->viewportSet = false;
	device->scissorSet = false;
}

void MGG_GraphicsDevice_SetConstantBuffer(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Buffer* buffer)
{
	assert(device != nullptr);
	assert(buffer != nullptr);

	// TODO: slot ??

	MGG_Buffer** uniforms = device->uniforms;

	if (uniforms[(int)stage] != buffer)
	{
		uniforms[(int)stage] = buffer;
		device->uniformsDirty |= 1 << (int)stage;
	}
	else
	{
		if (buffer->dirty)
			device->uniformsDirty |= 1 << (int)stage;
	}
}

void MGG_GraphicsDevice_SetTexture(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Texture* texture)
{
	assert(device != nullptr);
	assert(slot >= 0);
	assert(slot < MAX_TEXTURE_SLOTS);

	device->textures[(mgint)stage][slot] = texture;
	device->textureSamplerDirty |= 1 << slot;
}

void MGG_GraphicsDevice_SetSamplerState(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_SamplerState* state)
{
	assert(device != nullptr);
	assert(slot >= 0);
	assert(slot < MAX_TEXTURE_SLOTS);

	device->samplers[(mgint)stage][slot] = state;
	device->textureSamplerDirty |= 1 << slot;
}

void MGG_GraphicsDevice_SetIndexBuffer(MGG_GraphicsDevice* device, MGIndexElementSize size, MGG_Buffer* buffer)
{
	assert(device != nullptr);
	assert(buffer != nullptr);

	device->indexBuffer = buffer;
	device->indexBufferSize = size;
}

void MGG_GraphicsDevice_SetVertexBuffer(MGG_GraphicsDevice* device, mgint slot, MGG_Buffer* buffer, mgint vertexOffset)
{
	assert(device != nullptr);
	assert(buffer != nullptr);

	assert(slot >= 0 && slot < 8);

	device->vertexBuffers[slot] = buffer;
	device->vertexOffsets[slot] = vertexOffset;
	device->vertexBuffersDirty |= 1 << slot;
}

void MGG_GraphicsDevice_SetShader(MGG_GraphicsDevice* device, MGShaderStage stage, MGG_Shader* shader)
{
	assert(device != nullptr);
	assert(shader != nullptr);
	assert(shader->stage == stage);

	device->shaders[(mgint)stage] = shader;
	// Etervale fork: a compute shader is not part of the graphics program (MGG_GraphicsDevice_Dispatch binds it).
	if (stage != MGShaderStage::Compute)
		device->shaderDirty = true;
}

void MGG_GraphicsDevice_SetInputLayout(MGG_GraphicsDevice* device, MGG_InputLayout* layout)
{
	assert(device != nullptr);
	assert(layout != nullptr);

	if (layout != device->pipelineState.layout)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.layout = layout;
	}
}

void MGVK_GetTransition(	VkImageLayout oldLayout,
							VkImageLayout newLayout,
							VkAccessFlags& srcAccessMask,
							VkAccessFlags& dstAccessMask,
							VkPipelineStageFlags& sourceStage,
							VkPipelineStageFlags& destinationStage)
{
	if (oldLayout == VK_IMAGE_LAYOUT_UNDEFINED && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
	{
		srcAccessMask = 0;
		dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_UNDEFINED && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
	{
		srcAccessMask = 0;
		dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
		destinationStage |= VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
	{
		// Etervale fork: vertex shaders read uploaded textures too (terrain heights); upstream waited for the fragment stage only.
		srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
		dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;

		if (newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
			dstAccessMask |= VK_ACCESS_SHADER_WRITE_BIT;
	}
	else if (newLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
	{
		srcAccessMask = 0;
		dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
		destinationStage |= VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
	}
	else if (newLayout == VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
	{
		srcAccessMask = 0;
		dstAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
		destinationStage |= VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
		dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR)
	{
		srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		dstAccessMask = VK_ACCESS_MEMORY_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
		dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
		dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
		sourceStage |= VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
		dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_VERTEX_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else if (oldLayout == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && newLayout == VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)
	{
		srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
		dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
		sourceStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
		destinationStage |= VK_PIPELINE_STAGE_TRANSFER_BIT;
	}
	else
	{
		fprintf(stderr, "Warning: Potentially unhandled layout transition from %d to %d in %s:%d\n", oldLayout, newLayout, __FILE__, __LINE__);
		srcAccessMask = 0;
		dstAccessMask = 0;
		sourceStage |= VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
		destinationStage |= VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
	}
}

static void MGVK_CmdTransitionImageLayout(
	VkCommandBuffer cmd,
	VkImage image,
	VkImageLayout oldLayout,
	VkImageLayout newLayout,
	VkImageAspectFlags aspectMask,
	uint32_t baseMipLevel,
	uint32_t levelCount,
	uint32_t baseArrayLayer,
	uint32_t layerCount)
{
	VkImageMemoryBarrier barrier = {};
	barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
	barrier.oldLayout = oldLayout;
	barrier.newLayout = newLayout;
	barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.image = image;
	barrier.subresourceRange.aspectMask = aspectMask;
	barrier.subresourceRange.baseMipLevel = baseMipLevel;
	barrier.subresourceRange.levelCount = levelCount;
	barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
	barrier.subresourceRange.layerCount = layerCount;

	VkPipelineStageFlags sourceStage = 0;
	VkPipelineStageFlags destinationStage = 0;

	MGVK_GetTransition(oldLayout, newLayout, barrier.srcAccessMask, barrier.dstAccessMask, sourceStage, destinationStage);

	vkCmdPipelineBarrier(
		cmd,
		sourceStage, destinationStage,
		0,
		0, nullptr,
		0, nullptr,
		1, &barrier
	);
}

static void MGVK_CmdCopyBufferToImage(
	VkCommandBuffer cmds,
	VkBuffer buffer,
	VkImage image,
	int32_t x, int32_t y, int32_t z,
	int32_t level,
	int32_t slice,
	uint32_t width, uint32_t height, uint32_t depth)
{
	VkBufferImageCopy region = {};
	region.bufferOffset = 0;
	region.bufferRowLength = 0;
	region.bufferImageHeight = 0;

	region.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	region.imageSubresource.mipLevel = level;
	region.imageSubresource.baseArrayLayer = slice;
	region.imageSubresource.layerCount = 1;

	region.imageOffset = { x, y, z };
	region.imageExtent = { width, height, depth };

	vkCmdCopyBufferToImage(cmds, buffer, image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
}

static void MGVK_CmdCopyImageToBuffer(
	VkCommandBuffer cmds,
	VkImage image,
	VkBuffer buffer,
	int32_t x, int32_t y, int32_t z,
	int32_t level,
	int32_t slice,
	uint32_t width, uint32_t height, uint32_t depth,
	VkImageAspectFlags aspectMask)
{
	VkBufferImageCopy region = {};
	region.bufferOffset = 0;
	region.bufferRowLength = 0;
	region.bufferImageHeight = 0;

	region.imageSubresource.aspectMask = aspectMask;
	region.imageSubresource.mipLevel = level;
	region.imageSubresource.baseArrayLayer = slice;
	region.imageSubresource.layerCount = 1;

	region.imageOffset = { x, y, z };
	region.imageExtent = { width, height, depth };

	vkCmdCopyImageToBuffer(cmds, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, buffer, 1, &region);
}

static void MGVK_UpdateRenderPass(MGG_GraphicsDevice* device, FrameCounter currentFrame, VkCommandBuffer commandBuffer)
{
    const int MAX_ATTACHMENTS = 6;

	if (device->inRenderPass && !device->renderTargetDirty)
		return;

	MGVK_EndRenderPass(device, commandBuffer);

	// Are these targets being used for the first time this frame?
	for (int i = 0; i < MGVK_NUM_TARGETS; i++)
	{
		auto& target = device->targets.targets[i];
		if (target == nullptr)
		{
			device->targets.firstUse[i] = false;
			continue;
		}

		// A target keeps its content from earlier frames (OpenGL semantics: the TAA, cloud and IBL histories and the
		// banded shadow maps are drawn over across frames). Only a target never written starts undefined.
		device->targets.firstUse[i] = target->writeFrame == (FrameCounter)-1;

		// Mark the targets as being written to this frame.
		target->writeFrame = currentFrame;
	}

	// Lookup the texture set in the cache.
	const uint32_t hash = MGVK_HashTargetSet(device->targets);
	MGVK_TargetSetCache* cached = nullptr;
	{
		auto range = device->targetCache.equal_range(hash);
		for (auto itr = range.first; itr != range.second && !cached; ++itr)
		{
			if (MGVK_SameTargetSet(itr->second->set, device->targets))
				cached = itr->second;
		}
	}

	if (!cached)
	{
		cached = new MGVK_TargetSetCache();
		cached->set = device->targets;
		cached->uid = device->nextUid++;

		auto first = cached->set.targets[0];
		assert(first);
		cached->width = first->info.extent.width;
		cached->height = first->info.extent.height;
        bool isMsaa = first->multiSampleCount > 1;

        VkImageView attachments[MAX_ATTACHMENTS];
        VkAttachmentReference color_attachments[MAX_ATTACHMENTS];
		VkAttachmentReference depth_stencil_attachment;
        VkAttachmentReference resolve_attachment_ref = {};
        VkAttachmentDescription attachment_descs[MAX_ATTACHMENTS];
		memset(attachment_descs, 0, sizeof(attachment_descs));
		int num_attachments = 0;
		int num_color_attachments = 0;

		// Etervale fork: a depth surface (issue 016) is the pass's depth attachment and it has no colour attachment.
		const bool depthSurface = MGVK_IsDepthSurface(first);
		assert(!depthSurface || cached->set.numTargets == 1);
		for (int i = 0; i < cached->set.numTargets && !depthSurface; i++)
		{
			auto target = cached->set.targets[i];
			auto firstUse = cached->set.firstUse[i];
            auto layer = cached->set.arraySlices[i];
			assert(target);

            VkImageView viewToUse;
            if (target->isTarget && layer.has_value())
            {
                VkImageViewCreateInfo ivci = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
                ivci.image = isMsaa ? target->msImage : target->image; // Fix for MRT!
                ivci.viewType = VK_IMAGE_VIEW_TYPE_2D;
                ivci.format = target->info.format;
                ivci.subresourceRange.aspectMask = DetermineAspectMask(target->info.format);
                ivci.subresourceRange.baseMipLevel = 0;
                ivci.subresourceRange.levelCount = 1;
                ivci.subresourceRange.baseArrayLayer = layer.value();
                ivci.subresourceRange.layerCount = 1;

                VkResult res = vkCreateImageView(device->device, &ivci, nullptr, &viewToUse);
                VK_CHECK_RESULT(res);
                VK_SET_OBJECT_NAME(device->device, viewToUse, VK_OBJECT_TYPE_IMAGE_VIEW, "MGVK_TargetSetCache.arrayLayerViews[%d] (hash: %u)", i, hash);

                cached->arraySlicesViews[i] = viewToUse;
            }
            else
            {
                viewToUse = target->target_view;
                cached->arraySlicesViews[i] = std::nullopt;
            }
            attachments[num_attachments] = viewToUse;

			color_attachments[num_attachments].attachment = num_attachments;
			color_attachments[num_attachments].layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

            auto& desc = attachment_descs[num_attachments];
            desc.format = target->info.format;
            desc.samples = ToVkSampleCount(target->multiSampleCount);

            if (isMsaa)
            {
				// The multisampled image keeps its samples across render passes like any other target: a pass that
				// binds it again (after a compute dispatch, a copy or another target) draws over what is there.
				desc.loadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
				desc.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
				desc.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
				desc.stencilStoreOp = VK_ATTACHMENT_STORE_OP_STORE;
                desc.initialLayout = firstUse ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
				desc.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
            }
            else
            {
                desc.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
                desc.stencilStoreOp = VK_ATTACHMENT_STORE_OP_STORE;

                if (target->isSwapchain)
                {
                    desc.loadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
                    desc.stencilLoadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
                    desc.initialLayout = firstUse ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
                    desc.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
                }
                else
                {
                    desc.loadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
                    desc.stencilLoadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
                    desc.initialLayout = firstUse ? VK_IMAGE_LAYOUT_UNDEFINED : target->optimal_layout;
                    desc.finalLayout = target->optimal_layout;
                }
            }

			num_attachments++;
			num_color_attachments++;
		}

		if (depthSurface)
		{
			bool firstUse = cached->set.firstUse[0];
			depth_stencil_attachment.attachment = num_attachments;
			depth_stencil_attachment.layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
			attachments[num_attachments] = first->target_view;
			cached->arraySlicesViews[0] = std::nullopt;

			auto& desc = attachment_descs[num_attachments];
			desc.format = first->info.format;
			desc.samples = VK_SAMPLE_COUNT_1_BIT;
			desc.loadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
			desc.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
			desc.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
			desc.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
			desc.initialLayout = firstUse ? VK_IMAGE_LAYOUT_UNDEFINED : first->optimal_layout;
			desc.finalLayout = first->optimal_layout;
			num_attachments++;
		}

		auto depth = cached->set.targets[0]->depthTexture;
		if (depth)
		{
			bool firstUse = cached->set.firstUse[0];

			depth_stencil_attachment.attachment = num_attachments;
			depth_stencil_attachment.layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

			attachments[num_attachments] = depth->target_view;

            auto& desc = attachment_descs[num_attachments];
            desc.format = depth->info.format;
            desc.samples = ToVkSampleCount(depth->multiSampleCount);
            desc.loadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
            desc.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
            desc.stencilLoadOp = firstUse ? VK_ATTACHMENT_LOAD_OP_DONT_CARE : VK_ATTACHMENT_LOAD_OP_LOAD;
            desc.stencilStoreOp = VK_ATTACHMENT_STORE_OP_STORE;
            desc.initialLayout = firstUse ? VK_IMAGE_LAYOUT_UNDEFINED : VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
            desc.finalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
			num_attachments++;
		}

		if (isMsaa)
		{
			// Setup the resolve attachment which is the normal
			// color target without MSAA.

			auto firstTarget = cached->set.targets[0];
			attachments[num_attachments] = firstTarget->resolve_view;

			resolve_attachment_ref.attachment = num_attachments;
			resolve_attachment_ref.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

			auto& desc = attachment_descs[num_attachments];
			desc.format = firstTarget->info.format;
			desc.samples = VK_SAMPLE_COUNT_1_BIT;
			desc.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
			desc.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
			desc.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
			desc.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
			desc.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
			desc.finalLayout = firstTarget->isSwapchain ? VK_IMAGE_LAYOUT_PRESENT_SRC_KHR : firstTarget->optimal_layout;

			num_attachments++;
		}

		{
			VkSubpassDescription subpass_desc = {};
			subpass_desc.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
			subpass_desc.colorAttachmentCount = num_color_attachments;
			subpass_desc.pColorAttachments = color_attachments;

            if (isMsaa)
                subpass_desc.pResolveAttachments = &resolve_attachment_ref;

			if (depth || depthSurface)
				subpass_desc.pDepthStencilAttachment = &depth_stencil_attachment;
			cached->colorAttachments = num_color_attachments;

			// Add subpass dependencies for proper synchronization
			VkSubpassDependency dependencies[2];
			VkRenderPassCreateInfo create_info = { VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO };
			create_info.attachmentCount = num_attachments;
			create_info.pAttachments = attachment_descs;
			create_info.subpassCount = 1;
			create_info.pSubpasses = &subpass_desc;
			create_info.pDependencies = dependencies;
			if (first->isSwapchain)
			{
				create_info.dependencyCount = 1;

				dependencies[0].srcSubpass = VK_SUBPASS_EXTERNAL;
				dependencies[0].dstSubpass = 0;
				dependencies[0].srcStageMask =	VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
												VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
												VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
				dependencies[0].srcAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;

				dependencies[0].dstStageMask =	VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
												VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
												VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
				dependencies[0].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
												VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
												VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
												VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
				dependencies[0].dependencyFlags = VK_DEPENDENCY_BY_REGION_BIT;
			}
			else
			{
				create_info.dependencyCount = 2;

				dependencies[0].srcSubpass = VK_SUBPASS_EXTERNAL;
				dependencies[0].dstSubpass = 0;
				dependencies[0].srcStageMask =	VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
												VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT |
												VK_PIPELINE_STAGE_VERTEX_SHADER_BIT |
												VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
												VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
				dependencies[0].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
												VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;

				dependencies[0].dstStageMask =	VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT |
												VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
												VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
				dependencies[0].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
												VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
												VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
												VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
				dependencies[0].dependencyFlags = 0;

				dependencies[1].srcSubpass = 0;
				dependencies[1].dstSubpass = VK_SUBPASS_EXTERNAL;
				// Etervale fork: a depth surface (issue 016) is read by later shaders too, so its depth writes are made visible.
				dependencies[1].srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
				dependencies[1].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
				dependencies[1].dstStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT;
				dependencies[1].dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
				dependencies[1].dependencyFlags = 0;
			}

			VkResult res = vkCreateRenderPass(device->device, &create_info, nullptr, &cached->renderPass);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, cached->renderPass, VK_OBJECT_TYPE_RENDER_PASS, "MGVK_TargetSetCache.renderPass (hash: %u)", hash);
		}

		{
			VkFramebufferCreateInfo create_info = { VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO };
			create_info.renderPass = cached->renderPass;
			create_info.attachmentCount = num_attachments;
			create_info.pAttachments = attachments;
			create_info.width = cached->width;
			create_info.height = cached->height;
			create_info.layers = 1;

			VkResult res = vkCreateFramebuffer(device->device, &create_info, nullptr, &cached->framebuffer);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, cached->framebuffer, VK_OBJECT_TYPE_FRAMEBUFFER, "MGVK_TargetSetCache.framebuffer (hash: %u)", hash);
		}

		device->targetCache.emplace(hash, cached);
	}

	// Track our layout changes.
	for (int i = 0; i < cached->set.numTargets; i++)
	{
		auto target = cached->set.targets[i];
		if (target->isSwapchain)
			target->layouts[0] = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
		else
			target->layouts[0] = target->optimal_layout;
	}

	// Set the cache for the changed pipeline state.
	device->pipelineState.targets = cached;

	// Default viewport and scissor: the full target, unless the application set them after binding the targets (a
	// band of a shadow map, a tile of an atlas). Those stay; the flags keep them for later render passes on the same
	// targets.
	if (!device->viewportSet)
	{
		MGG_GraphicsDevice_SetViewport(device, 0, 0, cached->width, cached->height, 0, 1);
		device->viewportSet = false;
	}
	if (!device->scissorSet)
	{
		MGG_GraphicsDevice_SetScissorRectangle(device, 0, 0, cached->width, cached->height);
		device->scissorSet = false;
	}

	// Setup the render pass and pipeline.
	VkRect2D render_area;
	render_area.offset.x = 0;
	render_area.offset.y = 0;
	render_area.extent.width = cached->width;
	render_area.extent.height = cached->height;

	VkRenderPassBeginInfo render_pass_begin_info = { VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO };
	render_pass_begin_info.renderPass = cached->renderPass;
	render_pass_begin_info.framebuffer = cached->framebuffer;
	render_pass_begin_info.renderArea = render_area;

	render_pass_begin_info.clearValueCount = 0;
	render_pass_begin_info.pClearValues = NULL;

	VkQueryControlFlags flags = 0;
	if (device->deviceFeatures.occlusionQueryPrecise)
		flags = VK_QUERY_CONTROL_PRECISE_BIT;

	for (auto query : device->deferredOcclusionQueries)
	{
		if (!query->gpuHasBegun)
		{
			vkCmdResetQueryPool(commandBuffer, query->queryPool, 0, 1);
			query->gpuHasBegun = true;
		}
	}

	vkCmdBeginRenderPass(commandBuffer, &render_pass_begin_info, VK_SUBPASS_CONTENTS_INLINE);

	for (auto query : device->deferredOcclusionQueries)
		vkCmdBeginQuery(commandBuffer, query->queryPool, 0, flags);

	device->inRenderPass = true;
	device->renderTargetDirty = false;
	device->pipelineStateDirty = true;
	device->deferredOcclusionQueries.clear();
}

static const int DefaultPoolSize = 16384;

static void MGVK_FillDescriptorSetCache(MGG_GraphicsDevice* device, MGG_Shader* shader)
{
	// Pre-fill the free descriptor sets now.
	VkDescriptorSetAllocateInfo alloc_info = { VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO };
	alloc_info.descriptorPool = shader->pool;
	alloc_info.descriptorSetCount = 1;
	alloc_info.pSetLayouts = &shader->setLayout;
	for (int i = 0; i < DefaultPoolSize; i++)
	{
		MGVK_DescriptorInfo* info = new MGVK_DescriptorInfo;
		info->frame = 0;
		shader->freeSets.push(info);

		VkResult res = vkAllocateDescriptorSets(device->device, &alloc_info, &info->set);
		VK_CHECK_RESULT(res);
		VK_SET_OBJECT_NAME(device->device, info->set, VK_OBJECT_TYPE_DESCRIPTOR_SET, "MGVK_DescriptorInfo.set (Shader id: %u, index: %d)", shader->id, i);
	}
}

// Etervale fork (issues 005, 006): the view of one mip of a storage image, made the first time it is bound.
static VkImageView MGVK_StorageView(MGG_GraphicsDevice* device, MGG_Texture* texture, mgint mip)
{
	assert(texture->isStorage);
	assert(mip >= 0 && mip < (mgint)texture->info.mipLevels);
	auto& view = texture->storage_views[mip];
	if (view != VK_NULL_HANDLE)
		return view;

	VkImageViewCreateInfo info = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
	info.image = texture->image;
	info.viewType = ToVkImageViewType(texture->type, texture->info.arrayLayers);
	info.format = texture->info.format;
	info.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	info.subresourceRange.baseMipLevel = mip;
	info.subresourceRange.levelCount = 1;
	info.subresourceRange.baseArrayLayer = 0;
	info.subresourceRange.layerCount = texture->info.arrayLayers;
	VkResult res = vkCreateImageView(device->device, &info, nullptr, &view);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.storage_views[%d] (id: %llu)", mip, texture->id);
	return view;
}

static void MGVK_BindShaderResources(MGG_GraphicsDevice* device, FrameCounter currentFrame, MGG_Shader* shader, VkDescriptorSet* current, uint32_t* dynamicOffset);

static void MGVK_UpdateDescriptors(MGG_GraphicsDevice* device, FrameCounter currentFrame, MGG_Shader* shader, VkDescriptorSet* current, uint32_t* dynamicOffset)
{
	// If nothing is dirty then skip the update. (Etervale fork: uniformsDirty has one bit per shader stage, set by
	// MGG_GraphicsDevice_SetConstantBuffer, while uniformSlots is the shader's mask of constant-buffer slots. Masking one
	// with the other matched only the vertex stage (bit 0, slot 0): a pixel shader's new constants were ignored whenever
	// no texture or vertex-shader constant changed too, and the draw read the previous draw's values.)
	bool uniformsChanged = shader->uniformSlots != 0 && (device->uniformsDirty & (1u << (int)shader->stage)) != 0;
	uint32_t storageSlots = shader->bufferSlots | (shader->uavSlots << 16);
	if (!uniformsChanged &&
		(device->textureSamplerDirty & shader->textureSlots) == 0 &&
		(device->textureSamplerDirty & shader->samplerSlots) == 0 &&
		(device->storageDirty[(int)shader->stage] & storageSlots) == 0)
		return;

	MGVK_BindShaderResources(device, currentFrame, shader, current, dynamicOffset);
}

// The descriptor set of a shader for the resources bound now, found in the cache or written, and the shader's constants
// appended to the frame's uniform buffer. (Etervale fork: split from MGVK_UpdateDescriptors, which calls it only when
// something the shader reads changed, so a dispatch can call it every time; storage buffers and images included.)
static void MGVK_BindShaderResources(MGG_GraphicsDevice* device, FrameCounter currentFrame, MGG_Shader* shader, VkDescriptorSet* current, uint32_t* dynamicOffset)
{
	// If we got here we must have some sort of bindings!
	assert(!shader->bindings.empty());
	const int stage = (int)shader->stage;

	// Apply the bindings to the new descriptor set.
	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);
	auto& offset = frame.uniformOffset;
	auto buffer = frame.uniforms;

	// We have some dirty state... so the descriptor
	// needs to be updated before we draw.

	MGVK_DescriptorInfo* info;

	// First generate a hash of the new state, and the full key it stands for (Etervale fork).
	uint64_t key[4 + 2 * MAX_TEXTURE_SLOTS + MAX_TEXTURE_SLOTS + MGVK_MAX_UAV_SLOTS];
	int keyLength = 0;
	key[keyLength++] = (uint64_t)device->frameIndex;
	key[keyLength++] = shader->uniformSlots;
	key[keyLength++] = shader->textureSlots;
	uint32_t hash = MG_ComputeHash(shader->uniformSlots);
	hash = MG_ComputeHash(shader->textureSlots, hash);
	uint32_t dirty = shader->textureSlots;
	for (int i = 0; i < MAX_TEXTURE_SLOTS; i++)
	{
		uint32_t mask = 1 << i;
		if ((dirty & mask) == 0)
			continue;

		auto tex = device->textures[(mgint)shader->stage][i];
		if (!tex)
			hash = MG_ComputeHash(0, hash);
		else
		{
			tex->frame = currentFrame;
			hash = MG_ComputeHash(tex->id, hash);
		}
		key[keyLength++] = tex ? (uint64_t)tex->id : 0;

		auto sampler = device->samplers[(mgint)shader->stage][i];
		if (sampler)
			hash = MG_ComputeHash(sampler->id, hash);
		key[keyLength++] = sampler ? (uint64_t)sampler->id : 0;

		// Early out if there are no more used slots.
		dirty &= ~mask;
		if (!dirty)
			break;
	}

	// Etervale fork: the storage buffers and images by slot (issues 005, 006). Every resource the shader reads or writes
	// is stamped with this frame, so it is neither recycled nor destroyed while a frame in flight uses it.
	key[keyLength++] = (uint64_t)shader->bufferSlots | ((uint64_t)shader->uavSlots << 32);
	for (int i = 0; i < MAX_TEXTURE_SLOTS; i++)
	{
		if ((shader->bufferSlots & (1u << i)) == 0)
			continue;
		auto buffer = device->readBuffers[stage][i];
		if (buffer)
		{
			buffer->frame = currentFrame;
			buffer->used = true;
		}
		key[keyLength++] = buffer ? buffer->id : 0;
		hash = MG_ComputeHash((mguint)(key[keyLength - 1] & 0xFFFFFFFF), hash);
	}
	for (int i = 0; i < MGVK_MAX_UAV_SLOTS; i++)
	{
		if ((shader->uavSlots & (1u << i)) == 0)
			continue;
		auto& uav = device->uavs[stage][i];
		uint64_t id = 0;
		if (uav.texture)
		{
			uav.texture->frame = currentFrame;
			id = uav.texture->id | ((uint64_t)uav.mip << 48);
		}
		else if (uav.buffer)
		{
			uav.buffer->frame = currentFrame;
			uav.buffer->used = true;
			id = uav.buffer->id | (1ull << 63);
		}
		key[keyLength++] = id;
		hash = MG_ComputeHash((mguint)(id & 0xFFFFFFFF), hash);
		hash = MG_ComputeHash((mguint)(id >> 32), hash);
	}

	// We hash the frameIndex because each frame in the swap chain has its
	// own uniforms buffer (device->frames[frameIndex].uniforms) that gets
	// bound to the descriptor set. If we don't, some frame will use the
	// wrong buffer if the descriptor is retrieved from the cache (and will
	// result in flickers/broken rendering).
	//
	// TO DO: refactor the descriptor cache and uniforms buffer handling so
	// that we don't create twice as much descriptor due to this.
	hash = MG_ComputeHash(device->frameIndex, hash);

	// Do we have this same descriptor cached? (Etervale fork: the hash only narrows the search; the key decides.)
	info = nullptr;
	auto range = shader->usedSets.equal_range(hash);
	for (auto it = range.first; it != range.second; ++it)
	{
		auto& k = it->second->key;
		if ((int)k.size() == keyLength && std::equal(k.begin(), k.end(), key))
		{
			info = it->second;
			break;
		}
	}
	if (!info)
	{
		// The descriptor wasn't cached... so we need to
		// create a new one from the free sets.
		if (shader->freeSets.size() == 0)
		{
			// Allocate more cache if empty.
			MGVK_FillDescriptorSetCache(device, shader);
		}

		assert(shader->freeSets.size() > 0);

		info = shader->freeSets.front();
		shader->freeSets.pop();

		// Cache the new or recycled set for later use.
		info->key.assign(key, key + keyLength);
		shader->usedSets.emplace(hash, info);

		for (int i = 0; i < shader->bindings.size(); i++)
		{
			auto& w = shader->writes[i];
			w.dstSet = info->set;

			if (w.descriptorType == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC)
			{
				auto src = device->uniforms[(int)shader->stage + w.dstBinding];
				((VkDescriptorBufferInfo*)w.pBufferInfo)->buffer = buffer->buffer;
				((VkDescriptorBufferInfo*)w.pBufferInfo)->range = src->dataSize;
			}
			else if (w.descriptorType == VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER)
			{
				int slot = w.dstBinding - 32;
				((VkDescriptorImageInfo*)w.pImageInfo)->sampler = device->samplers[(mgint)shader->stage][slot]->sampler;

				auto tex = device->textures[(mgint)shader->stage][slot];
				if (tex)
					((VkDescriptorImageInfo*)w.pImageInfo)->imageView = tex->view;
				else
					((VkDescriptorImageInfo*)w.pImageInfo)->imageView = device->nullTexture[(int)shader->textureTypes[slot]]->view;
			}
			else if (w.descriptorType == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)
			{
				int slot = w.dstBinding - 32;

				auto tex = device->textures[(mgint)shader->stage][slot];
				if (tex)
					((VkDescriptorImageInfo*)w.pImageInfo)->imageView = tex->view;
				else
					((VkDescriptorImageInfo*)w.pImageInfo)->imageView = device->nullTexture[(int)shader->textureTypes[slot]]->view;
			}
			else if (w.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)
			{
				// Etervale fork: a read-only buffer (t-slot) or a RW buffer (u-slot); an empty read slot reads zeros.
				MGG_Buffer* buffer = w.dstBinding >= MGVK_UAV_BINDING
					? device->uavs[stage][w.dstBinding - MGVK_UAV_BINDING].buffer
					: device->readBuffers[stage][w.dstBinding - MGVK_TEXTURE_BINDING];
				if (!buffer)
					buffer = device->nullStorageBuffer;
				((VkDescriptorBufferInfo*)w.pBufferInfo)->buffer = buffer->buffer;
				((VkDescriptorBufferInfo*)w.pBufferInfo)->offset = 0;
				((VkDescriptorBufferInfo*)w.pBufferInfo)->range = VK_WHOLE_SIZE;
			}
			else if (w.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE)
			{
				// Etervale fork: one mip of a storage image; a dispatch with an empty image slot is refused before here.
				auto& uav = device->uavs[stage][w.dstBinding - MGVK_UAV_BINDING];
				assert(uav.texture != nullptr);
				((VkDescriptorImageInfo*)w.pImageInfo)->imageView = MGVK_StorageView(device, uav.texture, uav.mip);
			}
			else
			{
				// This should never happen!
				assert(0);
			}
		}

		vkUpdateDescriptorSets(device->device, shader->bindings.size(), shader->writes, 0, nullptr);
	}

	// Update the uniform data into the buffer.
	if (shader->uniformSlots)
	{
		for (int i = 0; i < shader->bindings.size(); i++)
		{
			auto& w = shader->writes[i];
			if (w.descriptorType == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC)
			{
				auto src = device->uniforms[(int)shader->stage + w.dstBinding];

				*dynamicOffset = offset;

				if ((offset + src->dataSize) < buffer->dataSize)
					MGVK_BufferCopyAndFlush(device, buffer, offset, src->push, src->dataSize);
				else
				{
					// TODO: Discard or wrap around?
					assert(0);
				}

				// Must meet the alignment limit for the device.
				auto alignedSize = MG_AlignUp(src->dataSize, (int)device->minUniformBufferOffsetAlignment);
				offset += alignedSize;
			}
		}
	}

	// Mark when we used this set last so we
	// can move it back the free list later.
	info->frame = currentFrame;
	*current = info->set;
}

static MGVK_Program* MGVK_ProgramGetOrCreate(MGG_GraphicsDevice* device, MGG_Shader* vertexShaderData, MGG_Shader* pixelShaderData)
{
	assert(device);
	assert(vertexShaderData);
	assert(pixelShaderData);

	uint64_t programId = ((uint64_t)vertexShaderData->id) | (((uint64_t)pixelShaderData->id) << 32);
	MGVK_Program* program = device->shader_programs[programId];
	if (program != nullptr)
		return program;

	program = new MGVK_Program();
	program->uid = programId;
	program->vertex = vertexShaderData;
	program->pixel = pixelShaderData;

	VkResult res;

	VkDescriptorSetLayout layouts[2];
	layouts[0] = program->vertex->setLayout;
	layouts[1] = program->pixel->setLayout;

	VkPipelineLayoutCreateInfo pipelineLayoutInfo;
	memset(&pipelineLayoutInfo, 0, sizeof(pipelineLayoutInfo));
	pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
	pipelineLayoutInfo.setLayoutCount = 2;
	pipelineLayoutInfo.pSetLayouts = layouts;
	pipelineLayoutInfo.pushConstantRangeCount = 0;
	pipelineLayoutInfo.pPushConstantRanges = nullptr;
	res = vkCreatePipelineLayout(device->device, &pipelineLayoutInfo, nullptr, &program->layout);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, program->layout, VK_OBJECT_TYPE_PIPELINE_LAYOUT, "MGVK_Program.layout (id: %llu)", programId);

	device->shader_programs[programId] = program;

	return program;
}

static VkPipeline MGVK_CreatePipeline(MGG_GraphicsDevice* device)
{
	// First see if we've cached this state before (Etervale fork: by the full key of object ids, see MGVK_PipelineKey).
	assert(device->pipelineState.blendState && device->pipelineState.rasterizerState && device->pipelineState.depthStencilState);
	const MGVK_PipelineKey key = MGVK_MakePipelineKey(device->pipelineState);
	const uint32_t hash = (uint32_t)MGVK_PipelineKeyHash()(key);
	auto itr = device->pipelines.find(key);
	if (itr != device->pipelines.end())
		return itr->second.cache;

	VkGraphicsPipelineCreateInfo pipelineInfo;
	memset(&pipelineInfo, 0, sizeof(pipelineInfo));
	pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
	if (device->pipelineStatistics)
		pipelineInfo.flags |= VK_PIPELINE_CREATE_CAPTURE_STATISTICS_BIT_KHR;   // Etervale fork, issue 012

	// We set the viewport and scissor dynamically.
	static const VkDynamicState dynamicStates[] = {
		VK_DYNAMIC_STATE_VIEWPORT,
		VK_DYNAMIC_STATE_SCISSOR,
		VK_DYNAMIC_STATE_BLEND_CONSTANTS,
		//VK_DYNAMIC_STATE_DEPTH_BIAS,
		//VK_DYNAMIC_STATE_DEPTH_BOUNDS,
	};
	VkPipelineDynamicStateCreateInfo dynamicState;
	memset(&dynamicState, 0, sizeof(dynamicState));
	dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
	dynamicState.dynamicStateCount = 3;
	dynamicState.pDynamicStates = dynamicStates;
	pipelineInfo.pDynamicState = &dynamicState;


	const auto& pstate = device->pipelineState;

	// NOTE: These don't matter as we set them dynamically.
	VkPipelineViewportStateCreateInfo viewport;
	memset(&viewport, 0, sizeof(viewport));
	viewport.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
	viewport.viewportCount = 1;
	viewport.pViewports = &device->viewport;
	viewport.scissorCount = 1;
	viewport.pScissors = &device->scissor;
	pipelineInfo.pViewportState = &viewport;

	VkPipelineShaderStageCreateInfo stages[2];
	memset(stages, 0, sizeof(stages));
	stages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	stages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
	stages[0].module = pstate.program->vertex->module;
	stages[0].pName = "main";
	stages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	stages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
	stages[1].module = pstate.program->pixel->module;
	stages[1].pName = "main";
	pipelineInfo.stageCount = 2;
	pipelineInfo.pStages = stages;
	pipelineInfo.layout = pstate.program->layout;

	VkPipelineVertexInputStateCreateInfo vertexInputInfo;
	memset(&vertexInputInfo, 0, sizeof(vertexInputInfo));
	vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
	vertexInputInfo.vertexBindingDescriptionCount = pstate.layout->bindings.size();
	vertexInputInfo.pVertexBindingDescriptions = pstate.layout->bindings.data();
	vertexInputInfo.vertexAttributeDescriptionCount = pstate.layout->attributes.size();
	vertexInputInfo.pVertexAttributeDescriptions = pstate.layout->attributes.data();
	pipelineInfo.pVertexInputState = &vertexInputInfo;

	VkPipelineInputAssemblyStateCreateInfo inputAssembly;
	memset(&inputAssembly, 0, sizeof(inputAssembly));
	inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
	inputAssembly.topology = pstate.topology;
	pipelineInfo.pInputAssemblyState = &inputAssembly;

	// Setup the blend, rasterizer, and depth/stencil states.
	VkPipelineColorBlendStateCreateInfo colorBlending = pstate.blendState->info;
	colorBlending.attachmentCount = pstate.targets->colorAttachments;
	pipelineInfo.pColorBlendState = &colorBlending;
	pipelineInfo.pRasterizationState = &pstate.rasterizerState->info;
	pipelineInfo.pDepthStencilState = &pstate.depthStencilState->info;

	// TODO: What state is this part of?
	VkPipelineMultisampleStateCreateInfo multisampling;
	memset(&multisampling, 0, sizeof(multisampling));
	multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
	multisampling.alphaToCoverageEnable = VK_FALSE; // Optional
	multisampling.alphaToOneEnable = VK_FALSE; // Optional
	multisampling.pSampleMask = nullptr; // Optional

	// Configure multisampling based on rasterizer state and render target
	if (pstate.rasterizerState->multiSampleAntiAlias && pstate.targets && pstate.targets->set.targets[0])
	{
		multisampling.rasterizationSamples = ToVkSampleCount(pstate.targets->set.targets[0]->multiSampleCount);

		// Enable sample shading for higher quality if supported by the device and MSAA is active
		if (device->deviceFeatures.sampleRateShading)
		{
			multisampling.sampleShadingEnable = VK_TRUE;
			multisampling.minSampleShading = 0.2f; // Run fragment shader on at least 20% of samples
		}
		else
		{
			multisampling.sampleShadingEnable = VK_FALSE;
			multisampling.minSampleShading = 1.0f;
		}
	}
	else
	{
		// No MSAA
		multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
		multisampling.sampleShadingEnable = VK_FALSE;
		multisampling.minSampleShading = 1.0f;
	}

	pipelineInfo.pMultisampleState = &multisampling;
	pipelineInfo.renderPass = pstate.targets->renderPass;

	pipelineInfo.subpass = 0;

	MGVK_PipelineCache pipeline;
	pipeline.state = device->pipelineState;

	VkResult res = vkCreateGraphicsPipelines(device->device, device->pipelineCache, 1, &pipelineInfo, nullptr, &pipeline.cache);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, pipeline.cache, VK_OBJECT_TYPE_PIPELINE, "MGVK_PipelineCache.cache (hash: %u)", hash);

	device->pipelines.emplace(key, pipeline);

	return pipeline.cache;
}

static void MGVK_UpdatePipeline(MGG_GraphicsDevice* device, VkCommandBuffer commandBuffer, FrameCounter currentFrame)
{
	assert(device);

	// Make sure we know when we last used these states. (Etervale fork: a state destroyed while current is cleared to
	// null; MonoGame sets one again before the next draw.)
	assert(device->pipelineState.blendState && device->pipelineState.depthStencilState && device->pipelineState.rasterizerState);
	if (device->pipelineState.blendState)
		device->pipelineState.blendState->frame = currentFrame;
	if (device->pipelineState.depthStencilState)
		device->pipelineState.depthStencilState->frame = currentFrame;
	if (device->pipelineState.rasterizerState)
		device->pipelineState.rasterizerState->frame = currentFrame;

	// Update the shader program.
	if (device->shaderDirty)
	{
		auto vertexShader = device->shaders[(mgint)MGShaderStage::Vertex];
		auto pixelShader = device->shaders[(mgint)MGShaderStage::Pixel];
		assert(vertexShader != nullptr);
		assert(pixelShader != nullptr);

		auto program = MGVK_ProgramGetOrCreate(device, vertexShader, pixelShader);

		if (program != device->pipelineState.program)
		{
			device->pipelineState.program = program;
			device->pipelineStateDirty = true;
		}

		device->shaderDirty = false;
	}

	// First update the pipeline if we need to.
	if (device->pipelineStateDirty)
	{
		device->pipelineStateDirty = false;

		auto pipeline = MGVK_CreatePipeline(device);
		vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
		device->lastGraphicsPipeline = pipeline;   // Etervale fork, issue 012

		// Since the pipeline changed we need to update other states.
		device->scissorDirty = true;
		device->uniformsDirty = 0xFFFFFFFF;
		device->textureSamplerDirty = 0xFFFFFFFF;
		device->storageDirty[(int)MGShaderStage::Vertex] = 0xFFFFFFFF;	// Etervale fork: a shader whose only resources
		device->storageDirty[(int)MGShaderStage::Pixel] = 0xFFFFFFFF;	// are storage buffers still gets its set
		device->descriptorSets[0] = nullptr;
		device->descriptorSets[1] = nullptr;
		device->dynamicOffsets[0] = 0;
		device->dynamicOffsets[1] = 0;
		device->vertexBuffersDirty = 0xFFFFFFFF;
		device->blendFactorDirty = true;

		// We need to re-apply the viewport too.
		vkCmdSetViewport(commandBuffer, 0, 1, &device->viewport);
	}

	if (device->scissorDirty)
	{
		device->scissorDirty = false;

		// If the scissor state is enabled then set the scissor rectable.
		auto rasterizerState = device->pipelineState.rasterizerState;
		if (rasterizerState->scissorTestEnable)
			vkCmdSetScissor(commandBuffer, 0, 1, &device->scissor);
		else
		{
			VkRect2D scissor;
			scissor.offset.x = 0;
			scissor.offset.y = 0;

			// When the scissor test is disabled we need to set the
			// scissor rectangle to the full size of the current target.
			auto targets = device->pipelineState.targets;
			scissor.extent.width = targets->width;
			scissor.extent.height = targets->height;

			vkCmdSetScissor(commandBuffer, 0, 1, &scissor);
		}
	}

	if (device->blendFactorDirty)
	{
		vkCmdSetBlendConstants(commandBuffer, device->blendFactor);
		device->blendFactorDirty = false;
	}

	// Do we need to update the descriptors? (Etervale fork: also when a storage buffer of the vertex or pixel stage
	// changed.)
	if (device->uniformsDirty || device->textureSamplerDirty ||
		device->storageDirty[(int)MGShaderStage::Vertex] || device->storageDirty[(int)MGShaderStage::Pixel])
	{
		auto program = device->pipelineState.program;

		// Update the shader bindings.
		int setCount = 0;
		int setOffset = 0;
		int offsetCount = 0;
		if (program->vertex->bindings.size() == 0)
			setOffset++;
		else
		{
			uint32_t* dynamicOffset = nullptr;
			if (program->vertex->uniformSlots)
				dynamicOffset = &device->dynamicOffsets[offsetCount++];

			MGVK_UpdateDescriptors(device, currentFrame, program->vertex, &device->descriptorSets[setCount++], dynamicOffset);
		}

		if (program->pixel->bindings.size() > 0)
		{
			uint32_t* dynamicOffset = nullptr;
			if (program->pixel->uniformSlots)
				dynamicOffset = &device->dynamicOffsets[offsetCount++];

			MGVK_UpdateDescriptors(device, currentFrame, program->pixel, &device->descriptorSets[setCount++], dynamicOffset);
		}

		// Bind the new descriptor sets.
		vkCmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS,
			program->layout, setOffset, setCount, device->descriptorSets, offsetCount, device->dynamicOffsets);

		// Clear all the dirty flags.
		device->uniformsDirty = 0;
		device->textureSamplerDirty = 0;
		device->storageDirty[(int)MGShaderStage::Vertex] = 0;
		device->storageDirty[(int)MGShaderStage::Pixel] = 0;
	}

	if (device->vertexBuffersDirty)
	{
		// TODO: Fix multiple vertex streams!

		int count = 0;
		VkDeviceSize offsets[8];
		VkBuffer buffers[8];
		for (int i = 0; i < 8; i++)
		{
			offsets[i] = device->vertexOffsets[i];
			buffers[i] = nullptr;

			auto buffer = device->vertexBuffers[i];
			if (buffer)
			{
				buffer->frame = currentFrame;
				buffers[i] = buffer->buffer;
				count++;
			}
		}

		vkCmdBindVertexBuffers(commandBuffer, 0, count, buffers, offsets);

		device->vertexBuffersDirty = 0;
	}
}

static int MGVK_GetIndexCount(MGPrimitiveType primitiveType, mgint primitiveCount)
{
	switch (primitiveType)
	{
	case MGPrimitiveType::LineList:
		return primitiveCount * 2;
	case MGPrimitiveType::LineStrip:
		return primitiveCount + 1;
	case MGPrimitiveType::TriangleList:
		return primitiveCount * 3;
	case MGPrimitiveType::TriangleStrip:
		return primitiveCount + 2;
	default:
	case MGPrimitiveType::PointList:
		return primitiveCount;
	}
}

// Etervale fork: every draw appends its constants to the frame's uniform buffer (32 MB), which only starts over at Present.
// Rendering several frames' worth of draws before one Present (a capture that renders the scene repeatedly into an
// off-screen target) ran past its end: the constants were not written and the dynamic offsets pointed beyond the buffer
// (VUID-vkCmdBindDescriptorSets-pDescriptorSets-01979). When less than a draw's worth is left, the commands recorded so
// far are submitted and waited for, and the buffer starts over; the next draw rebinds everything.
static void MGVK_EnsureUniformSpace(MGG_GraphicsDevice* device, MGVK_Frame& frame)
{
	const mgint reserve = 1024 * 1024;
	if (frame.uniforms == nullptr || (mgint)frame.uniformOffset + reserve <= frame.uniforms->dataSize)
		return;

	static int reported = 0;
	if (reported < 5)
	{
		reported++;
		printf("Uniform buffer full in frame %llu: the commands so far are submitted early\n", (unsigned long long)device->frame);
	}
	MGVK_FlushCommands(device, frame);
	MGVK_BeginCommandBuffer(frame.commandBuffer);
	frame.uniformOffset = 0;
	device->renderTargetDirty = true;
	device->pipelineStateDirty = true;
}

// Etervale fork: every buffer a draw reads carries the frame number, so a later SetData can tell whether a frame still on
// the GPU reads it (MGG_Buffer_SetData) and a discarded or destroyed buffer is reused or freed only after that frame.
// Upstream stamped vertex buffers only when they were rebound and index buffers never.
static void MGVK_StampDrawBuffers(MGG_GraphicsDevice* device, bool indexed)
{
	for (int i = 0; i < 8; i++)
	{
		if (auto buffer = device->vertexBuffers[i])
		{
			buffer->frame = device->frame;
			buffer->used = true;
		}
	}
	if (indexed && device->indexBuffer != nullptr)
	{
		device->indexBuffer->frame = device->frame;
		device->indexBuffer->used = true;
	}
}

void MGG_GraphicsDevice_Draw(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, mgint vertexStart, mgint vertexCount)
{
	assert(device != nullptr);
	assert(vertexStart >= 0);

	if (vertexCount <= 0)
		return;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	MGVK_EnsureUniformSpace(device, frame);
	MGVK_UpdateRenderPass(device, device->frame, frame.commandBuffer);

	auto topology = ToVkPrimitiveTopology(primitiveType);
	if (device->pipelineState.topology != topology)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.topology = topology;
	}

	MGVK_UpdatePipeline(device, frame.commandBuffer, device->frame);
	MGVK_StampDrawBuffers(device, false);

	vkCmdDraw(frame.commandBuffer, vertexCount, 1, vertexStart, 0);
}

void MGG_GraphicsDevice_DrawIndexed(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, mgint primitiveCount, mgint indexStart, mgint vertexStart)
{
	assert(device != nullptr);
	assert(primitiveCount >= 0);
	assert(indexStart >= 0);
	assert(vertexStart >= 0);

	if (primitiveCount <= 0)
		return;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	MGVK_EnsureUniformSpace(device, frame);
	MGVK_UpdateRenderPass(device, device->frame, frame.commandBuffer);

	auto topology = ToVkPrimitiveTopology(primitiveType);
	if (device->pipelineState.topology != topology)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.topology = topology;
	}

	MGVK_UpdatePipeline(device, frame.commandBuffer, device->frame);
	MGVK_StampDrawBuffers(device, true);

	auto indexBuffer = device->indexBuffer;
	assert(indexBuffer != nullptr);

	// TODO: Detect if we need to rebind the same index buffer?
	vkCmdBindIndexBuffer(frame.commandBuffer, indexBuffer->buffer, 0, device->indexBufferSize == MGIndexElementSize::SixteenBits ? VK_INDEX_TYPE_UINT16 : VK_INDEX_TYPE_UINT32);

	auto indexCount = MGVK_GetIndexCount(primitiveType, primitiveCount);

	vkCmdDrawIndexed(frame.commandBuffer, indexCount, 1, indexStart, vertexStart, 0);
}

void MGG_GraphicsDevice_DrawIndexedInstanced(
	MGG_GraphicsDevice* device,
	MGPrimitiveType primitiveType,
	mgint primitiveCount,
	mgint indexStart,
	mgint vertexStart,
	mgint baseInstance,
	mgint instanceCount)
{
	assert(device != nullptr);
	assert(indexStart >= 0);
	assert(vertexStart >= 0);
	assert(baseInstance >= 0);
	assert(instanceCount > 0);

    if (primitiveCount <= 0 || instanceCount <= 0)
        return;

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	MGVK_EnsureUniformSpace(device, frame);
	MGVK_UpdateRenderPass(device, device->frame, frame.commandBuffer);

	auto topology = ToVkPrimitiveTopology(primitiveType);
	if (device->pipelineState.topology != topology)
	{
		device->pipelineStateDirty = true;
		device->pipelineState.topology = topology;
	}

	MGVK_UpdatePipeline(device, frame.commandBuffer, device->frame);
	MGVK_StampDrawBuffers(device, true);

	auto indexBuffer = device->indexBuffer;
	assert(indexBuffer != nullptr);
	vkCmdBindIndexBuffer(frame.commandBuffer, indexBuffer->buffer, 0, device->indexBufferSize == MGIndexElementSize::SixteenBits ? VK_INDEX_TYPE_UINT16 : VK_INDEX_TYPE_UINT32);

	auto indexCount = MGVK_GetIndexCount(primitiveType, primitiveCount);

	vkCmdDrawIndexed(
		frame.commandBuffer,
		indexCount,
		instanceCount,
		indexStart,
		vertexStart,
		baseInstance);
}

inline mgint getMipScalar(mgint level, mgint value)
{
	value = value >> level;
	return value < 1 ? 1 : value;
}

inline mgint getMipScalar(mgint level, mgint value, mgint alignment)
{
	alignment -= 1;
	return ((value >> level) + alignment) & ~alignment;
}

uint32_t getVkFormatBlockAlignment(VkFormat format) {
	switch (format) {
		// BCn (DXT) Formats
	case VK_FORMAT_BC1_RGB_UNORM_BLOCK:
	case VK_FORMAT_BC1_RGB_SRGB_BLOCK:
	case VK_FORMAT_BC1_RGBA_UNORM_BLOCK:
	case VK_FORMAT_BC1_RGBA_SRGB_BLOCK:
	case VK_FORMAT_BC2_UNORM_BLOCK:
	case VK_FORMAT_BC2_SRGB_BLOCK:
	case VK_FORMAT_BC3_UNORM_BLOCK:
	case VK_FORMAT_BC3_SRGB_BLOCK:
	case VK_FORMAT_BC4_UNORM_BLOCK:
	case VK_FORMAT_BC4_SNORM_BLOCK:
	case VK_FORMAT_BC5_UNORM_BLOCK:
	case VK_FORMAT_BC5_SNORM_BLOCK:
	case VK_FORMAT_BC6H_UFLOAT_BLOCK:
	case VK_FORMAT_BC6H_SFLOAT_BLOCK:
	case VK_FORMAT_BC7_UNORM_BLOCK:
	case VK_FORMAT_BC7_SRGB_BLOCK:
		// ETC2/EAC Format
	case VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK:
	case VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK:
	case VK_FORMAT_ETC2_R8G8B8A1_UNORM_BLOCK:
	case VK_FORMAT_ETC2_R8G8B8A1_SRGB_BLOCK:
	case VK_FORMAT_ETC2_R8G8B8A8_UNORM_BLOCK:
	case VK_FORMAT_ETC2_R8G8B8A8_SRGB_BLOCK:
	case VK_FORMAT_EAC_R11_UNORM_BLOCK:
	case VK_FORMAT_EAC_R11_SNORM_BLOCK:
	case VK_FORMAT_EAC_R11G11_UNORM_BLOCK:
	case VK_FORMAT_EAC_R11G11_SNORM_BLOCK:
		// ASTC Format
	case VK_FORMAT_ASTC_4x4_UNORM_BLOCK:
	case VK_FORMAT_ASTC_4x4_SRGB_BLOCK:
		return 4;
	case VK_FORMAT_ASTC_5x5_UNORM_BLOCK:
	case VK_FORMAT_ASTC_5x5_SRGB_BLOCK:
		return 5;
	case VK_FORMAT_ASTC_6x6_UNORM_BLOCK:
	case VK_FORMAT_ASTC_6x6_SRGB_BLOCK:
		return 6;
	case VK_FORMAT_ASTC_8x8_UNORM_BLOCK:
	case VK_FORMAT_ASTC_8x8_SRGB_BLOCK:
		return 8;
	case VK_FORMAT_ASTC_10x10_UNORM_BLOCK:
	case VK_FORMAT_ASTC_10x10_SRGB_BLOCK:
		return 10;
	case VK_FORMAT_ASTC_12x12_UNORM_BLOCK:
	case VK_FORMAT_ASTC_12x12_SRGB_BLOCK:
		return 12;

	default:
		return 1;
	}
}

void MGG_GraphicsDevice_ResolveRenderTargets(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);

    auto psoTargets = device->pipelineState.targets;
    if (!psoTargets)
        return;

	// We resolve MSAA and mips to the active command buffer.
	auto& frame = device->frames[device->frameIndex];
	bool endPass = true;

    for (int i = 0; i < psoTargets->set.numTargets; ++i)
    {
        MGG_Texture* renderTarget = psoTargets->set.targets[i];

        if (renderTarget == nullptr || renderTarget->isSwapchain)
            continue;

		// We should be recording if we're going to resolve mips here.
		assert(frame.is_recording);
		if (endPass)
		{
			MGVK_EndRenderPass(device, frame.commandBuffer);
			endPass = false;
		}

        if (renderTarget->info.mipLevels > 1)
        {
            VkImageMemoryBarrier barrier = {};
            barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            barrier.image = renderTarget->image;
            barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            barrier.subresourceRange.baseArrayLayer = 0;
            barrier.subresourceRange.layerCount = renderTarget->info.arrayLayers;
            barrier.subresourceRange.levelCount = 1;

            int32_t mipWidth = renderTarget->info.extent.width;
            int32_t mipHeight = renderTarget->info.extent.height;

            for (uint32_t j = 1; j < renderTarget->info.mipLevels; j++)
            {
                barrier.subresourceRange.baseMipLevel = j - 1;

                barrier.oldLayout = (j == 1) ? renderTarget->layouts[0] : VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                barrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;

                VkPipelineStageFlags srcStage;
                if (j == 1)
                {
                    if (barrier.oldLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)
                    {
                        barrier.srcAccessMask = 0;
                        srcStage =	VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT |
                                    VK_PIPELINE_STAGE_VERTEX_SHADER_BIT;
                    }
                    else
                    {
                        barrier.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
                        srcStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
                    }
                }
                else
                {
                    barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                    srcStage = VK_PIPELINE_STAGE_TRANSFER_BIT;
                }
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;

                vkCmdPipelineBarrier(frame.commandBuffer,
                    srcStage,
                    VK_PIPELINE_STAGE_TRANSFER_BIT,
                    0, 0, nullptr, 0, nullptr, 1, &barrier);

                VkImageBlit blit = {};
                blit.srcOffsets[0] = {0, 0, 0};
                blit.srcOffsets[1] = {mipWidth, mipHeight, 1};
                blit.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                blit.srcSubresource.mipLevel = j - 1;
                blit.srcSubresource.baseArrayLayer = 0;
                blit.srcSubresource.layerCount = renderTarget->info.arrayLayers;

                int32_t nextMipWidth = mipWidth > 1 ? mipWidth / 2 : 1;
                int32_t nextMipHeight = mipHeight > 1 ? mipHeight / 2 : 1;

                blit.dstOffsets[0] = {0, 0, 0};
                blit.dstOffsets[1] = {nextMipWidth, nextMipHeight, 1};
                blit.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                blit.dstSubresource.mipLevel = j;
                blit.dstSubresource.baseArrayLayer = 0;
                blit.dstSubresource.layerCount = renderTarget->info.arrayLayers;

                barrier.subresourceRange.baseMipLevel = j;
                barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
                barrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                barrier.srcAccessMask = 0;
                barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;

                vkCmdPipelineBarrier(frame.commandBuffer,
                    VK_PIPELINE_STAGE_TRANSFER_BIT,
                    VK_PIPELINE_STAGE_TRANSFER_BIT,
                    0, 0, nullptr, 0, nullptr, 1, &barrier);

                vkCmdBlitImage(frame.commandBuffer,
                    renderTarget->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    renderTarget->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1, &blit,
                    VK_FILTER_LINEAR);

                barrier.subresourceRange.baseMipLevel = j - 1;
                barrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
                barrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                barrier.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
                barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;

                vkCmdPipelineBarrier(frame.commandBuffer,
                    VK_PIPELINE_STAGE_TRANSFER_BIT,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                    0, 0, nullptr, 0, nullptr, 1, &barrier);

                mipWidth = nextMipWidth;
                mipHeight = nextMipHeight;
            }

            barrier.subresourceRange.baseMipLevel = renderTarget->info.mipLevels - 1;
            barrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            barrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;

            vkCmdPipelineBarrier(frame.commandBuffer,
                VK_PIPELINE_STAGE_TRANSFER_BIT,
                VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                0, 0, nullptr, 0, nullptr, 1, &barrier);

            renderTarget->layouts[0] = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        }
    }
}


void MGG_GraphicsDevice_GetBackBufferData(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height, void* data, mgint count, mgint dataBytes)
{
	assert(device != nullptr);
	assert(data != nullptr);
	assert(count > 0);
	assert(dataBytes > 0);

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	MGVK_EndRenderPass(device, frame.commandBuffer);

	VK_CHECK_RESULT(vkEndCommandBuffer(frame.commandBuffer));

	VkPipelineStageFlags waitStages[] = { VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT };

	VkSubmitInfo flushSubmitInfo = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
	flushSubmitInfo.waitSemaphoreCount = 1;
	flushSubmitInfo.pWaitSemaphores = &frame.imageAcquiredSemaphore;
	flushSubmitInfo.commandBufferCount = 1;
	flushSubmitInfo.pCommandBuffers = &frame.commandBuffer;
	flushSubmitInfo.pWaitDstStageMask = waitStages;
	flushSubmitInfo.signalSemaphoreCount = 1;
	flushSubmitInfo.pSignalSemaphores = &device->swapchains[frame.image_index].renderCompleteSemaphore;

	VkFence flushFence;
	VkFenceCreateInfo fenceInfo = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
	vkCreateFence(device->device, &fenceInfo, nullptr, &flushFence);
	VK_SET_OBJECT_NAME(device->device, flushFence, VK_OBJECT_TYPE_FENCE, "MGG_GraphicsDevice_GetBackBufferData::flushFence");

	{
		std::lock_guard lock(device->queueMutex);
		vkQueueSubmit(device->queue, 1, &flushSubmitInfo, flushFence);
	}
	vkWaitForFences(device->device, 1, &flushFence, VK_TRUE, UINT64_MAX);
	vkDestroyFence(device->device, flushFence, nullptr);

	auto srcImage = device->swapchains[frame.image_index].texture->image;

	MGG_Texture* tempRgbaTexture = MGG_Texture_Create(
		device,
		MGTextureType::_2D,
		MGSurfaceFormat::Color,
		width,
		height,
		1,
		1,
		1
	);
	VK_SET_OBJECT_NAME(device->device, tempRgbaTexture->image, VK_OBJECT_TYPE_IMAGE, "GetBackBufferData::tempRgbaImage");

	uint32_t bytesPerPixelOnGpu = 4;
	VkDeviceSize gpuBufferSize = (VkDeviceSize)width * height * bytesPerPixelOnGpu;
	VkBuffer dstBuffer;
	VmaAllocation dstBufferAllocation;
	VmaAllocationInfo dstAllocInfo = {};
	{
		VkBufferCreateInfo bufferInfo = { VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
		bufferInfo.size = gpuBufferSize;
		bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;

		VmaAllocationCreateInfo allocCreateInfo = {};
		allocCreateInfo.usage = VMA_MEMORY_USAGE_GPU_TO_CPU;
		allocCreateInfo.flags = VMA_ALLOCATION_CREATE_MAPPED_BIT;

		VK_CHECK_RESULT(vmaCreateBuffer(device->allocator, &bufferInfo, &allocCreateInfo, &dstBuffer, &dstBufferAllocation, &dstAllocInfo));
		VK_SET_OBJECT_NAME(device->device, dstBuffer, VK_OBJECT_TYPE_BUFFER, "MGG_GraphicsDevice_GetBackBufferData::dstBuffer");
	}

	VkCommandBuffer copyCmdBuffer = MGVK_BeginNewCommandBuffer(device);
	{
		MGVK_CmdTransitionImageLayout(copyCmdBuffer, srcImage, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);
		MGVK_CmdTransitionImageLayout(copyCmdBuffer, tempRgbaTexture->image, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);

		VkImageBlit imageBlitRegion = {};
		imageBlitRegion.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
		imageBlitRegion.srcSubresource.layerCount = 1;
		imageBlitRegion.srcOffsets[0] = { x, y, 0 };
		imageBlitRegion.srcOffsets[1] = { x + (int32_t)width, y + (int32_t)height, 1 };
		imageBlitRegion.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
		imageBlitRegion.dstSubresource.layerCount = 1;
		imageBlitRegion.dstOffsets[0] = { 0, 0, 0 };
		imageBlitRegion.dstOffsets[1] = { (int32_t)width, (int32_t)height, 1 };
		vkCmdBlitImage(copyCmdBuffer, srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, tempRgbaTexture->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &imageBlitRegion, VK_FILTER_NEAREST);

		MGVK_CmdTransitionImageLayout(copyCmdBuffer, tempRgbaTexture->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);
		MGVK_CmdTransitionImageLayout(copyCmdBuffer, srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_ASPECT_COLOR_BIT);

		VkBufferImageCopy copyRegion = {};
		copyRegion.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
		copyRegion.imageSubresource.layerCount = 1;
		copyRegion.imageOffset = { 0, 0, 0 };
		copyRegion.imageExtent = { (uint32_t)width, (uint32_t)height, 1 };
		vkCmdCopyImageToBuffer(copyCmdBuffer, tempRgbaTexture->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, dstBuffer, 1, &copyRegion);
	}

    MGVK_ExecuteAndFreeCommandBuffer(device, copyCmdBuffer);

	vmaInvalidateAllocation(device->allocator, dstBufferAllocation, 0, VK_WHOLE_SIZE);
	assert(dstAllocInfo.pMappedData != nullptr);

	size_t bytesToCopy = (size_t)count * dataBytes;
	if (bytesToCopy > gpuBufferSize)
	{
		bytesToCopy = gpuBufferSize;
	}

	memcpy(data, dstAllocInfo.pMappedData, bytesToCopy);

	vmaDestroyBuffer(device->allocator, dstBuffer, dstBufferAllocation);
	vkDestroyImageView(device->device, tempRgbaTexture->view, nullptr);
	vmaDestroyImage(device->allocator, tempRgbaTexture->image, tempRgbaTexture->allocation);
	delete tempRgbaTexture;

	MGVK_BeginCommandBuffer(frame.commandBuffer);
	device->renderTargetDirty = true;
}

static VkBlendFactor ToVkBlendFactor(MGBlend mode)
{
	switch (mode)
	{
	case MGBlend::One:
		return VK_BLEND_FACTOR_ONE;
	case MGBlend::Zero:
		return VK_BLEND_FACTOR_ZERO;
	case MGBlend::SourceColor:
		return VK_BLEND_FACTOR_SRC_COLOR;
	case MGBlend::InverseSourceColor:
		return VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;
	case MGBlend::SourceAlpha:
		return VK_BLEND_FACTOR_SRC_ALPHA;
	case MGBlend::InverseSourceAlpha:
		return VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
	case MGBlend::DestinationColor:
		return VK_BLEND_FACTOR_DST_COLOR;
	case MGBlend::InverseDestinationColor:
		return VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR;
	case MGBlend::DestinationAlpha:
		return VK_BLEND_FACTOR_DST_ALPHA;
	case MGBlend::InverseDestinationAlpha:
		return VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA;
	case MGBlend::BlendFactor:
		return VK_BLEND_FACTOR_CONSTANT_COLOR;
	case MGBlend::InverseBlendFactor:
		return VK_BLEND_FACTOR_ONE_MINUS_CONSTANT_COLOR;
	case MGBlend::SourceAlphaSaturation:
		return VK_BLEND_FACTOR_SRC_ALPHA_SATURATE;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkBlendOp ToVkBlendOp(MGBlendFunction func)
{
	switch (func)
	{
	case MGBlendFunction::Add:
		return VK_BLEND_OP_ADD;
	case MGBlendFunction::Subtract:
		return VK_BLEND_OP_SUBTRACT;
	case MGBlendFunction::ReverseSubtract:
		return VK_BLEND_OP_REVERSE_SUBTRACT;
	case MGBlendFunction::Min:
		return VK_BLEND_OP_MIN;
	case MGBlendFunction::Max:
		return VK_BLEND_OP_MAX;
	default:
		MGVK_UNREACHABLE();
	}
}

static void MGVK_SetBlendState(MGG_BlendState_Info* info, VkPipelineColorBlendAttachmentState* blend)
{
	if ((int)info->colorWriteChannels & (int)MGColorWriteChannels::Red)
		blend->colorWriteMask |= VK_COLOR_COMPONENT_R_BIT;
	if ((int)info->colorWriteChannels & (int)MGColorWriteChannels::Green)
		blend->colorWriteMask |= VK_COLOR_COMPONENT_G_BIT;
	if ((int)info->colorWriteChannels & (int)MGColorWriteChannels::Blue)
		blend->colorWriteMask |= VK_COLOR_COMPONENT_B_BIT;
	if ((int)info->colorWriteChannels & (int)MGColorWriteChannels::Alpha)
		blend->colorWriteMask |= VK_COLOR_COMPONENT_A_BIT;

	blend->blendEnable = !(info->colorSourceBlend == MGBlend::One &&
		info->colorDestBlend == MGBlend::Zero &&
		info->alphaSourceBlend == MGBlend::One &&
		info->alphaDestBlend == MGBlend::Zero);

	blend->srcColorBlendFactor = ToVkBlendFactor(info->colorSourceBlend);
	blend->dstColorBlendFactor = ToVkBlendFactor(info->colorDestBlend);
	blend->colorBlendOp = ToVkBlendOp(info->colorBlendFunc);
	blend->srcAlphaBlendFactor = ToVkBlendFactor(info->alphaSourceBlend);
	blend->dstAlphaBlendFactor = ToVkBlendFactor(info->alphaDestBlend);
	blend->alphaBlendOp = ToVkBlendOp(info->alphaBlendFunc);
}

MGG_BlendState* MGG_BlendState_Create(MGG_GraphicsDevice* device, MGG_BlendState_Info* infos)
{
	assert(device != nullptr);
	assert(infos != nullptr);

	// First check the cache.
	uint32_t hash = MG_ComputeHash((mgbyte*)infos, sizeof(MGG_BlendState_Info) * 4);
	MGG_BlendState* cached = MGVK_FindState(device->blendStates, hash, infos, sizeof(MGG_BlendState_Info) * 4);
	if (cached)
	{
		cached->refs++;
		return cached;
	}

	auto state = new MGG_BlendState();
	memset(state, 0, sizeof(MGG_BlendState));

	auto attachments = state->attachments;
	MGVK_SetBlendState(infos + 0, attachments + 0);
	MGVK_SetBlendState(infos + 1, attachments + 1);
	MGVK_SetBlendState(infos + 2, attachments + 2);
	MGVK_SetBlendState(infos + 3, attachments + 3);

	auto& blend = state->info;
	blend.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
	blend.logicOpEnable = VK_FALSE;
	blend.logicOp = VK_LOGIC_OP_COPY;
	blend.flags = 0; // Reserved for future use.
	blend.pAttachments = state->attachments;
	blend.attachmentCount = 4;

	state->refs = 1;
	state->hash = hash;
	state->uid = device->nextUid++;
	memcpy(state->source, infos, sizeof(MGG_BlendState_Info) * 4);

	device->blendStates.emplace(hash, state);

	return state;

}

void MGG_BlendState_Destroy(MGG_GraphicsDevice* device, MGG_BlendState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (!state)
		return;

	// Release the reference.
	state->refs--;
	if (state->refs > 0)
		return;

	// Queue the state for later destruction.
	device->destroyBlendStates.push(state);
	MGVK_ForgetState(device->blendStates, state);
}

static VkCompareOp ToVkCompareOp(MGCompareFunction func)
{
	switch (func)
	{
	case MGCompareFunction::Always:
		return VK_COMPARE_OP_ALWAYS;
	case MGCompareFunction::Never:
		return VK_COMPARE_OP_NEVER;
	case MGCompareFunction::Less:
		return VK_COMPARE_OP_LESS;
	case MGCompareFunction::LessEqual:
		return VK_COMPARE_OP_LESS_OR_EQUAL;
	case MGCompareFunction::Equal:
		return VK_COMPARE_OP_EQUAL;
	case MGCompareFunction::GreaterEqual:
		return VK_COMPARE_OP_GREATER_OR_EQUAL;
	case MGCompareFunction::Greater:
		return VK_COMPARE_OP_GREATER;
	case MGCompareFunction::NotEqual:
		return VK_COMPARE_OP_NOT_EQUAL;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkStencilOp ToVkStencilOp(MGStencilOperation op)
{
	switch (op)
	{
	case MGStencilOperation::Keep:
		return VK_STENCIL_OP_KEEP;
	case MGStencilOperation::Zero:
		return VK_STENCIL_OP_ZERO;
	case MGStencilOperation::Replace:
		return VK_STENCIL_OP_REPLACE;
	case MGStencilOperation::Increment:
		return VK_STENCIL_OP_INCREMENT_AND_WRAP;
	case MGStencilOperation::Decrement:
		return VK_STENCIL_OP_DECREMENT_AND_WRAP;
	case MGStencilOperation::IncrementSaturation:
		return VK_STENCIL_OP_INCREMENT_AND_CLAMP;
	case MGStencilOperation::DecrementSaturation:
		return VK_STENCIL_OP_DECREMENT_AND_CLAMP;
	case MGStencilOperation::Invert:
		return VK_STENCIL_OP_INVERT;
	default:
		MGVK_UNREACHABLE();
	}
}

MGG_DepthStencilState* MGG_DepthStencilState_Create(MGG_GraphicsDevice* device, MGG_DepthStencilState_Info* info)
{
	assert(device != nullptr);
	assert(info != nullptr);

	// First check the cache.
	uint32_t hash = MG_ComputeHash((mgbyte*)info, sizeof(MGG_DepthStencilState_Info));
	MGG_DepthStencilState* cached = MGVK_FindState(device->depthStencilStates, hash, info, sizeof(MGG_DepthStencilState_Info));
	if (cached)
	{
		cached->refs++;
		return cached;
	}

	auto state = new MGG_DepthStencilState();
	memset(state, 0, sizeof(MGG_DepthStencilState));

	auto& depth = state->info;
	depth.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
	depth.flags = 0; // Reserved for future use.
	depth.depthTestEnable = info->depthBufferEnable;
	depth.depthWriteEnable = info->depthBufferWriteEnable;
	depth.depthCompareOp = ToVkCompareOp(info->depthBufferFunction);
	depth.depthBoundsTestEnable = VK_FALSE; // VK_TRUE;
	depth.stencilTestEnable = info->stencilEnable;
	depth.front.failOp = ToVkStencilOp(info->stencilFail);
	depth.front.passOp = ToVkStencilOp(info->stencilPass);
	depth.front.depthFailOp = ToVkStencilOp(info->stencilDepthBufferFail);
	depth.front.compareOp = ToVkCompareOp(info->stencilFunction);
	depth.front.compareMask = info->stencilMask;
	depth.front.writeMask = info->stencilWriteMask;
	depth.front.reference = info->referenceStencil;
	if (info->twoSidedStencilMode)
	{
		depth.back.failOp = ToVkStencilOp(info->counterClockwiseStencilFail);
		depth.back.passOp = ToVkStencilOp(info->counterClockwiseStencilPass);
		depth.back.depthFailOp = ToVkStencilOp(info->counterClockwiseStencilDepthBufferFail);
		depth.back.compareOp = ToVkCompareOp(info->counterClockwiseStencilFunction);
		depth.back.compareMask = info->stencilMask;
		depth.back.writeMask = info->stencilWriteMask;
		depth.back.reference = info->referenceStencil;
	}
	else
	{
		depth.back = depth.front;
	}

	depth.minDepthBounds = 0.0f;
	depth.maxDepthBounds = 1.0f;

	state->refs = 1;
	state->hash = hash;
	state->uid = device->nextUid++;
	state->source = *info;
	device->depthStencilStates.emplace(hash, state);

	return state;
}

void MGG_DepthStencilState_Destroy(MGG_GraphicsDevice* device, MGG_DepthStencilState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (!state)
		return;

	// Release the reference.
	state->refs--;
	if (state->refs > 0)
		return;

	// Queue the state for later destruction.
	device->destroyDepthStencilStates.push(state);
	MGVK_ForgetState(device->depthStencilStates, state);
}

static VkPolygonMode ToVkPolygonMode(MGFillMode mode)
{
	switch (mode)
	{
	case MGFillMode::Solid:
		return VK_POLYGON_MODE_FILL;
	case MGFillMode::WireFrame:
		return VK_POLYGON_MODE_LINE;
	default:
		MGVK_UNREACHABLE();
	}
}

static VkCullModeFlags ToVkCullModeFlags(MGCullMode mode)
{
	switch (mode)
	{
	case MGCullMode::None:
		return VK_CULL_MODE_NONE;
	case MGCullMode::CullClockwiseFace:
		return VK_CULL_MODE_FRONT_BIT;
	case MGCullMode::CullCounterClockwiseFace:
		return VK_CULL_MODE_BACK_BIT;
	default:
		MGVK_UNREACHABLE();
	}
}


MGG_RasterizerState* MGG_RasterizerState_Create(MGG_GraphicsDevice* device, MGG_RasterizerState_Info* info)
{
	assert(device != nullptr);
	assert(info != nullptr);

	// First check the cache.
	uint32_t hash = MG_ComputeHash((mgbyte*)info, sizeof(MGG_RasterizerState_Info));
	MGG_RasterizerState* cached = MGVK_FindState(device->rasterizerStates, hash, info, sizeof(MGG_RasterizerState_Info));
	if (cached)
	{
		cached->refs++;
		return cached;
	}

	auto state = new MGG_RasterizerState();
	memset(state, 0, sizeof(MGG_RasterizerState));

	auto& rasterizer = state->info;
	rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
	rasterizer.flags = 0; // Reserved for future use.
	rasterizer.depthClampEnable = !info->depthClipEnable;
	rasterizer.rasterizerDiscardEnable = VK_FALSE;
	rasterizer.polygonMode = ToVkPolygonMode(info->fillMode);
	rasterizer.cullMode = ToVkCullModeFlags(info->cullMode);
	rasterizer.frontFace = VK_FRONT_FACE_CLOCKWISE;
	rasterizer.depthBiasEnable = info->depthBias != 0;
	rasterizer.depthBiasConstantFactor = info->depthBias * ((1 << 24) - 1);
	rasterizer.depthBiasClamp = 0.0f;
	rasterizer.depthBiasSlopeFactor = info->slopeScaleDepthBias;
	rasterizer.lineWidth = 1.0f;

	// These don't fit this structure, so we
	// need to hold them for later.
	state->scissorTestEnable = info->scissorTestEnable;
	state->multiSampleAntiAlias = info->multiSampleAntiAlias;

	state->refs = 1;
	state->hash = hash;
	state->uid = device->nextUid++;
	state->source = *info;
	device->rasterizerStates.emplace(hash, state);

	return state;
}

void MGG_RasterizerState_Destroy(MGG_GraphicsDevice* device, MGG_RasterizerState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (!state)
		return;

	// Release the reference.
	state->refs--;
	if (state->refs > 0)
		return;

	// Queue the state for later destruction.
	device->destroyRasterizerStates.push(state);
	MGVK_ForgetState(device->rasterizerStates, state);
}

static VkSamplerAddressMode ToVkSamplerAddressMode(MGTextureAddressMode mode)
{
	switch (mode)
	{
	case MGTextureAddressMode::Wrap:
		return VK_SAMPLER_ADDRESS_MODE_REPEAT;
	case MGTextureAddressMode::Clamp:
		return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
	case MGTextureAddressMode::Mirror:
		return VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT;
	case MGTextureAddressMode::Border:
		return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_BORDER;
	default:
		MGVK_UNREACHABLE();
	}
}

MGG_SamplerState* MGG_SamplerState_Create(MGG_GraphicsDevice* device, MGG_SamplerState_Info* info)
{
	assert(device != nullptr);
	assert(info != nullptr);

	// TODO: Why isn't this reference counted?

	auto state = new MGG_SamplerState();
	state->info = *info; // For debugging

	bool isComparison = info->FilterMode == MGTextureFilterMode::Comparison;

	VkSamplerCreateInfo samplerInfo = { VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO };
	samplerInfo.addressModeU = ToVkSamplerAddressMode(info->AddressU);
	samplerInfo.addressModeV = ToVkSamplerAddressMode(info->AddressV);
	samplerInfo.addressModeW = ToVkSamplerAddressMode(info->AddressW);
	samplerInfo.anisotropyEnable =	info->Filter == MGTextureFilter::Anisotropic &&
									device->deviceFeatures.samplerAnisotropy == VK_TRUE;
	samplerInfo.maxAnisotropy = info->MaximumAnisotropy;
	samplerInfo.unnormalizedCoordinates = VK_FALSE;
	samplerInfo.compareEnable = isComparison ? VK_TRUE : VK_FALSE;
	samplerInfo.compareOp = isComparison ? ToVkCompareOp(info->ComparisonFunction) : VK_COMPARE_OP_NEVER;
	samplerInfo.mipLodBias = info->MipMapLevelOfDetailBias;
	samplerInfo.minLod = info->MaxMipLevel;
	samplerInfo.maxLod = VK_LOD_CLAMP_NONE;

	VkSamplerCustomBorderColorCreateInfoEXT bcolor = {};

	// TODO: What about the FLOAT color cases?
	if (info->BorderColor == 0x00000000)
		samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_TRANSPARENT_BLACK;
	else if (info->BorderColor == 0xFF000000)
		samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
	else if (info->BorderColor == 0xFFFFFFFF)
		samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_WHITE;
	else
	{
		if (!device->customBorderColorSupported)
		{
			samplerInfo.borderColor = VK_BORDER_COLOR_INT_TRANSPARENT_BLACK;
		}
		else
		{
			samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_CUSTOM_EXT;
			bcolor.sType = VK_STRUCTURE_TYPE_SAMPLER_CUSTOM_BORDER_COLOR_CREATE_INFO_EXT;

			// RGBA
			bcolor.format = VK_FORMAT_UNDEFINED;
			bcolor.customBorderColor.float32[0] = ((info->BorderColor >> 0) & 0xFF) / 255.0f;
			bcolor.customBorderColor.float32[1] = ((info->BorderColor >> 8) & 0xFF) / 255.0f;
			bcolor.customBorderColor.float32[2] = ((info->BorderColor >> 16) & 0xFF) / 255.0f;
			bcolor.customBorderColor.float32[3] = ((info->BorderColor >> 24) & 0xFF) / 255.0f;

			samplerInfo.pNext = &bcolor;
		}
	}

	switch (info->Filter)
	{
	case MGTextureFilter::Anisotropic:
	case MGTextureFilter::Linear:
		samplerInfo.minFilter = VK_FILTER_LINEAR;
		samplerInfo.magFilter = VK_FILTER_LINEAR;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
		break;
	case MGTextureFilter::LinearMipPoint:
		samplerInfo.minFilter = VK_FILTER_LINEAR;
		samplerInfo.magFilter = VK_FILTER_LINEAR;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
		break;
	case MGTextureFilter::Point:
		samplerInfo.minFilter = VK_FILTER_NEAREST;
		samplerInfo.magFilter = VK_FILTER_NEAREST;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
		break;
	case MGTextureFilter::PointMipLinear:
		samplerInfo.minFilter = VK_FILTER_NEAREST;
		samplerInfo.magFilter = VK_FILTER_NEAREST;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
		break;
	case MGTextureFilter::MinLinearMagPointMipLinear:
		samplerInfo.minFilter = VK_FILTER_LINEAR;
		samplerInfo.magFilter = VK_FILTER_NEAREST;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
		break;
	case MGTextureFilter::MinLinearMagPointMipPoint:
		samplerInfo.minFilter = VK_FILTER_LINEAR;
		samplerInfo.magFilter = VK_FILTER_NEAREST;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
		break;
	case MGTextureFilter::MinPointMagLinearMipLinear:
		samplerInfo.minFilter = VK_FILTER_NEAREST;
		samplerInfo.magFilter = VK_FILTER_LINEAR;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
		break;
	case MGTextureFilter::MinPointMagLinearMipPoint:
		samplerInfo.minFilter = VK_FILTER_NEAREST;
		samplerInfo.magFilter = VK_FILTER_LINEAR;
		samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
		break;
	default:
		assert(0);
	}

	VkResult res = vkCreateSampler(device->device, &samplerInfo, nullptr, &state->sampler);
	VK_CHECK_RESULT(res);

	state->id = ++device->currentSamplerId;
	VK_SET_OBJECT_NAME(device->device, state->sampler, VK_OBJECT_TYPE_SAMPLER, "MGG_SamplerState.sampler (id: %llu)", state->id);

	return state;
}

void MGG_SamplerState_Destroy(MGG_GraphicsDevice* device, MGG_SamplerState* state)
{
	assert(device != nullptr);
	assert(state != nullptr);

	if (!state)
		return;

	device->destroySamplers.push(state);
}

// Etervale fork: moves the discarded buffers that no frame in flight reads any more to the free list. Called when a frame
// slot is about to record again, after its fence was waited: frame F's work is complete once frame F + swapchainCount
// begins (in-order submission on one queue).
static void MGVK_RecycleDiscardedBuffers(MGG_GraphicsDevice* device)
{
	MGG_Buffer** link = &device->pending;
	while (*link != nullptr)
	{
		auto buffer = *link;
		if (buffer->used && buffer->frame + device->swapchainCount > device->frame)
		{
			link = &buffer->next;
			continue;
		}
		*link = buffer->next;
		buffer->next = device->free;
		device->free = buffer;
	}
}

static MGG_Buffer* MGVK_BufferDiscard(MGG_GraphicsDevice* device, MGG_Buffer* buffer)
{
	// Get the info we need to find/allocate a new buffer.
	auto dataSize = buffer->dataSize;
	auto type = buffer->type;

	// Discard the current buffer.
	assert(buffer->next == nullptr);
	buffer->next = device->discarded;
	device->discarded = buffer;
	buffer = nullptr;

	// Search for the best fit from the free list.
	MGG_Buffer** curr = &device->free;
	MGG_Buffer** best = nullptr;
	while (*curr != nullptr)
	{
		if ((*curr)->type == type)
		{
			auto currSize = (*curr)->actualSize;

			if (currSize >= dataSize)
			{
				if (best == nullptr || (*best)->actualSize > currSize)
				{
					best = curr;
					if (currSize == dataSize)
						break;
				}
			}
		}

		curr = &(*curr)->next;
	}

	// Did we find a match?
	if (best != nullptr)
	{
		buffer = *best;
		*best = buffer->next;
		buffer->next = nullptr;
		buffer->dataSize = dataSize;
		buffer->used = false;
		//buffer->dirtyMin = 0;
		//buffer->dirtyMax = 0;
		return buffer;
	}

	// We didn't find a match, so allocate a new one.
	auto dynamic = type == MGBufferType::Constant;
	buffer = MGG_Buffer_Create(device, type, dynamic, dataSize);

	return buffer;
}

static MGG_Buffer* MGVK_Buffer_Create(MGG_GraphicsDevice* device, MGBufferType type, mgint sizeInBytes, bool no_push)
{
	assert(device != nullptr);

	auto buffer = new MGG_Buffer();
	memset(buffer, 0, sizeof(MGG_Buffer));

	buffer->actualSize = buffer->dataSize = sizeInBytes;
	buffer->type = type;

	if (type == MGBufferType::Storage)
	{
		// Etervale fork (issue 006): device-local, never mapped; filled through staging copies
		// (MGG_Buffer_SetStorageData) and cleared to zero once here, so a buffer read before its first write reads zeros.
		MGVK_BufferCreate(device, sizeInBytes, MGVK_StorageBufferUsage, VMA_MEMORY_USAGE_GPU_ONLY, buffer);
		buffer->id = device->nextUid++;
		{
			std::lock_guard lock(device->transferMutex);
			VkCommandBuffer cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
			vkCmdFillBuffer(cmd, buffer->buffer, 0, VK_WHOLE_SIZE, 0);
			VkBufferMemoryBarrier barrier = { VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
			barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
			barrier.dstAccessMask = MGVK_StorageReadAccess;
			barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
			barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
			barrier.buffer = buffer->buffer;
			barrier.size = VK_WHOLE_SIZE;
			vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, MGVK_StorageStages, 0, 0, nullptr, 1, &barrier, 0, nullptr);
			MGVK_ExecuteAndFreeCommandBuffer(device, cmd, device->transferPool);
		}
		device->all_buffers.push_back(buffer);
		return buffer;
	}

	if (type == MGBufferType::Constant && !no_push)
	{
		// Uniform buffers never use real buffers.  We just
		// keep the data in system memory until it is time to render.
		buffer->push = new mgbyte[sizeInBytes];
	}
	else
	{
		VkBufferUsageFlags usage;
		switch (type)
		{
		case MGBufferType::Index:
			usage = VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
			break;
		case MGBufferType::Vertex:
			usage = VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
			break;
		case MGBufferType::Constant:
			usage = VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
			break;
		default:
			assert(0);
		}

		MGVK_BufferCreate(device, sizeInBytes, usage, VMA_MEMORY_USAGE_CPU_TO_GPU, buffer);

		// TODO: We could lazy map on first write/read... then
		// unmap it if we haven't accessed it in a while.  This
		// should reduce the CPU memory we have in use for
		// generally static buffers.  Maybe the dynamic buffer
		// hint would be useful for this?

		// Mapping buffers takes a good amount of time, so
		// just keep the buffer mapped the whole time and
		// use flush/invalidate to syncronize as needed.
		auto res = vmaMapMemory(device->allocator, buffer->allocation, (void**)&buffer->mapped);
		VK_CHECK_RESULT(res);
	}

	device->all_buffers.push_back(buffer);

	return buffer;
}

MGG_Buffer* MGG_Buffer_Create(MGG_GraphicsDevice* device, MGBufferType type, mgbool dynamic, mgint sizeInBytes)
{
	return MGVK_Buffer_Create(device, type, sizeInBytes, !dynamic);
}

static void MGVK_BufferCopyAndFlush(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int destOffset, mgbyte* data, int dataBytes)
{
	assert(device);
	assert(buffer);
	assert(destOffset < buffer->dataSize);
	assert(destOffset + dataBytes <= buffer->dataSize);

	memcpy(buffer->mapped + destOffset, data, dataBytes);
	buffer->dirty = false;
}

static void MGVK_BufferCopyAndFlush(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int destOffset, mgbyte* data, mgint dataCount, mgint dataBytes, mgint dataStride)
{
	assert(device);
	assert(buffer);
	assert(destOffset < buffer->dataSize);
	assert(dataStride >= dataBytes);
	assert(destOffset + dataCount * dataStride + dataBytes - dataStride <= buffer->dataSize);

	mgbyte* dest_ptr = buffer->mapped + destOffset;

	if (dataStride == dataBytes)
	{
		memcpy(dest_ptr, data, dataCount * dataBytes);
	}
	else
	{
		for (mgint i = 0; i < dataCount; ++i)
		{
			memcpy(dest_ptr + i * dataStride,
				   data + i * dataBytes,
				   dataBytes);
		}
	}

	buffer->dirty = false;
}

void MGG_Buffer_Destroy(MGG_GraphicsDevice* device, MGG_Buffer* buffer)
{
	assert(device != nullptr);
	assert(buffer != nullptr);

	if (!buffer)
		return;

	mg_remove(device->all_buffers, buffer);

	// Push buffers can be freed immediately.
	if (buffer->push)
	{
		delete [] buffer->push;
		delete buffer;
		return;
	}

	// Safe to unmap it now.
	if (buffer->mapped)
		vmaUnmapMemory(device->allocator, buffer->allocation);

	// Queue the buffer for later destruction.
	device->destroyBuffers.push(buffer);
}


void MGG_Buffer_SetData(MGG_GraphicsDevice* device, MGG_Buffer*& buffer, mgint offset, mgbyte* data, mgint elementCount, mgint vertexStride, mgint elementSizeInBytes, mgbool discard)
{
	assert(device != nullptr);
	assert(buffer != nullptr);
	assert(data != nullptr);
	assert(offset >= 0);
	assert(elementCount > 0);
	assert(vertexStride > 0);
	assert(elementSizeInBytes > 0);

	buffer->dirty = true;

	// If this is a push buffer we don't need to
	// do anything other than copy over the content.
	// We can safely ignore the discard.
	if (buffer->push)
	{
		if (elementSizeInBytes == vertexStride)
		{
			memcpy(buffer->push + offset, data, elementCount * elementSizeInBytes);
		}
		else
		{
			for (mgint i = 0; i < elementCount; ++i)
			{
				memcpy(buffer->push + offset + i * vertexStride,
					   data + i * elementSizeInBytes,
					   elementSizeInBytes);
			}
		}
		return;
	}

	// Etervale fork: a buffer that a frame still in flight (or a draw already recorded in this one) reads is not written in
	// place: those draws must keep the old content, as in OpenGL, where SetData is ordered with the draws. The update goes
	// to a renamed buffer that starts as a copy of the old one (a partial update keeps the rest). Upstream wrote the
	// persistently mapped memory the GPU was reading.
	bool inFlight = buffer->used && buffer->frame + device->swapchainCount > device->frame;
	mgbyte* previous = nullptr;
	if (!discard && inFlight)
	{
		previous = buffer->mapped;
		discard = true;
	}

	if (discard)
	{
		auto last = buffer;

		buffer = MGVK_BufferDiscard(device, buffer);
		if (previous != nullptr && buffer->mapped != nullptr)
			memcpy(buffer->mapped, previous, std::min(last->dataSize, buffer->dataSize));

		// Fix any active mapping of the buffer that
		// was just discarded for another.

		switch (buffer->type)
		{
		case MGBufferType::Constant:
			for (int i=0; i < (int)MGShaderStage::Count; i++)
			{
				if (device->uniforms[i] == last)
				{
					device->uniforms[i] = buffer;
					device->uniformsDirty |= 1 << (int)i;
				}
			}
			break;

		case MGBufferType::Vertex:
			for (int i = 0; i < 8; i++)
			{
				if (device->vertexBuffers[i] == last)
				{
					device->vertexBuffers[i] = buffer;
					device->vertexBuffersDirty |= 1 << i;
				}
			}
			break;

		case MGBufferType::Index:
			if (device->indexBuffer == last)
				device->indexBuffer = buffer;
			break;
		}
	}

	// Do the copy and flush.
	MGVK_BufferCopyAndFlush(device, buffer, offset, data, elementCount, elementSizeInBytes, vertexStride);
}

// Etervale fork (issue 013): SetDataOptions.NoOverwrite. The caller promises the range it writes is not read by any
// recorded or in-flight draw (the framework's ring of user vertices and indices for DrawUser*Primitives appends this
// way), so the data goes into the buffer in place: no rename, no copy of the old content. MGG_Buffer_SetData without
// discard renames a buffer a frame still reads and copies its whole content back from mapped memory first, which made
// every user-primitive draw after the first in a frame cost about 0.8 ms (SpriteBatch with many textures).
MG_EXPORT void MGG_Buffer_SetDataNoOverwrite(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint elementCount, mgint vertexStride, mgint elementSizeInBytes)
{
	assert(device != nullptr);
	assert(buffer != nullptr);
	assert(data != nullptr);
	assert(offset >= 0);
	assert(elementCount > 0);
	assert(vertexStride > 0);
	assert(elementSizeInBytes > 0);

	buffer->dirty = true;
	if (buffer->push)
	{
		for (mgint i = 0; i < elementCount; ++i)
			memcpy(buffer->push + offset + i * vertexStride, data + i * elementSizeInBytes, elementSizeInBytes);
		return;
	}

	MGVK_BufferCopyAndFlush(device, buffer, offset, data, elementCount, elementSizeInBytes, vertexStride);
}

void MGG_Buffer_GetData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataCount, mgint dataBytes, mgint dataStride)
{
    assert(device != nullptr);
    assert(buffer != nullptr);
    assert(data != nullptr);
    assert(dataCount > 0);
    assert(dataBytes > 0);
    assert(dataStride > 0);

    // Note: Reading back from mapped memory is VERY slow and
    // there are not good optimizations that don't involve CPU
    // side caching or GPU readback which has latency.

    mgbyte* src_ptr = buffer->push ? buffer->push : buffer->mapped;
    src_ptr += offset;

    if (dataStride == dataBytes)
    {
        memcpy(data, src_ptr, dataCount * dataBytes);
    }
    else
    {
        auto bytesToCopy = dataBytes < dataStride ? dataBytes : dataStride;
        for (mgint i = 0; i < dataCount; ++i)
        {
            memcpy(data, src_ptr, bytesToCopy);
            data += dataBytes;
            src_ptr += dataStride;
        }
    }
}

static MGG_Texture* MGVK_Texture_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices,
	bool storage);

MGG_Texture* MGG_Texture_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices)
{
	return MGVK_Texture_Create(device, type, format, width, height, depth, mipmaps, slices, false);
}

// Etervale fork (issue 005): a texture shaders can also write (RWTexture2D): the image gets the storage usage. Null when
// the device cannot use the format as a storage image (vkGetPhysicalDeviceFormatProperties).
MG_EXPORT MGG_Texture* MGG_Texture_CreateStorage(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices)
{
	assert(device != nullptr);
	VkFormatProperties props = {};
	vkGetPhysicalDeviceFormatProperties(device->physicalDevice, ToVkFormat(format), &props);
	if ((props.optimalTilingFeatures & VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT) == 0)
		return nullptr;
	return MGVK_Texture_Create(device, type, format, width, height, depth, mipmaps, slices, true);
}

static MGG_Texture* MGVK_Texture_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices,
	bool storage)
{
	assert(device != nullptr);

	assert(width > 0);
	assert(height > 0);
	assert(depth > 0);
	assert(mipmaps > 0);
	assert(slices > 0);
	assert(type != MGTextureType::Cube || (slices % 6) == 0);

	auto texture = new MGG_Texture();
	texture->type = type;
	texture->format = format;
	texture->id = ++device->currentTextureId;
	texture->isStorage = storage;

	VkImageCreateInfo& create_info = texture->info;
	create_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	create_info.imageType = ToVkImageType(type);
	create_info.flags = ToVkImageCreateFlags(type);

	create_info.format = ToVkFormat(format);
	create_info.extent.width = width;
	create_info.extent.height = height;
	create_info.extent.depth = depth;
	create_info.mipLevels = mipmaps;
	create_info.arrayLayers = slices;
	create_info.samples = VK_SAMPLE_COUNT_1_BIT;
	create_info.tiling = VK_IMAGE_TILING_OPTIMAL;
	create_info.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
	if (storage)
		create_info.usage |= VK_IMAGE_USAGE_STORAGE_BIT;
	create_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
	create_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

	mggCreateImage(device, &create_info, texture);
	VK_SET_OBJECT_NAME(device->device, texture->image, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.image (id: %llu)", texture->id);

	texture->view = CreateImageView(device, texture, mipmaps);
	VK_SET_OBJECT_NAME(device->device, texture->view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.view (id: %llu)", texture->id);

	device->all_textures.push_back(texture);

	// Start the texture in optimal layout mode.
	texture->optimal_layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

	VkCommandBuffer cmd;
	{
		std::lock_guard lock(device->transferMutex);

		cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
		MGVK_CmdTransitionImageLayout(cmd, texture->image,
			VK_IMAGE_LAYOUT_UNDEFINED, texture->optimal_layout,
			VK_IMAGE_ASPECT_COLOR_BIT, 0, mipmaps, 0, slices);

		MGVK_ExecuteAndFreeCommandBuffer(device, cmd, device->transferPool);
	}
	for (int i = 0; i < mipmaps; i++)
		texture->layouts[i] = texture->optimal_layout;

	return texture;
}

MGG_Texture* MGG_RenderTarget_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices,
	MGDepthFormat depthFormat,
	mgint multiSampleCount,
	MGRenderTargetUsage usage)
{
	assert(device != nullptr);

	assert(width > 0);
	assert(height > 0);
	assert(depth > 0);
	assert(mipmaps > 0);
	assert(slices > 0);
	assert(type != MGTextureType::Cube || (slices % 6) == 0);

	auto texture = new MGG_Texture();
	texture->isTarget = true;
	texture->type = type;
	texture->format = format;
	texture->id = ++device->currentTextureId;

	texture->depthFormat = depthFormat;
	texture->multiSampleCount = multiSampleCount;
	texture->usage = usage;

	{
		VkImageCreateInfo& create_info = texture->info;
		create_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
		create_info.imageType = ToVkImageType(type);
		create_info.flags = ToVkImageCreateFlags(type);
		create_info.format = ToVkFormat(format);
		create_info.extent.width = width;
		create_info.extent.height = height;
		create_info.extent.depth = depth;
		create_info.mipLevels = mipmaps;
		create_info.arrayLayers = slices;
        create_info.samples = VK_SAMPLE_COUNT_1_BIT;
		create_info.tiling = VK_IMAGE_TILING_OPTIMAL;
		create_info.usage = VK_IMAGE_USAGE_SAMPLED_BIT |
                        VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT |
                        VK_IMAGE_USAGE_TRANSFER_SRC_BIT |
                        VK_IMAGE_USAGE_TRANSFER_DST_BIT;
		// Etervale fork: a depth surface (issue 016) is its own depth attachment: one level, one sample, no separate depth
		// buffer. Between passes it rests sampled, in the read-only layout its descriptors expect.
		if (MGVK_IsDepthSurface(texture))
		{
			assert(type == MGTextureType::_2D && mipmaps == 1 && multiSampleCount <= 1 && depthFormat == MGDepthFormat::None);
			create_info.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
		}
		create_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
		create_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

		// Start where a render target rests between passes (its optimal layout), so one sampled before it is first drawn
		// (a history on its first frame) is in the layout its descriptor expects. Its first render pass starts from
		// UNDEFINED anyway.
		VkImageLayout startLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
		mggCreateImage(device, &create_info, texture);
		VK_SET_OBJECT_NAME(device->device, texture->image, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.image (RenderTarget id: %llu)", texture->id);

		texture->view = CreateImageView(device, texture, create_info.mipLevels);
		VK_SET_OBJECT_NAME(device->device, texture->view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.view (RenderTarget id: %llu)", texture->id);

		if (multiSampleCount > 1)
		{
			auto ms_create_info = create_info;
			ms_create_info.samples = ToVkSampleCount(multiSampleCount);
			ms_create_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
			VmaAllocationCreateInfo allocInfo = {};
			allocInfo.usage = VMA_MEMORY_USAGE_GPU_ONLY;
			VkResult res = vmaCreateImage(device->allocator, &ms_create_info, &allocInfo,
				&texture->msImage, &texture->msAllocation, nullptr);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, texture->msImage, VK_OBJECT_TYPE_IMAGE,
				"MGG_Texture.msImage (RenderTarget id: %llu)", texture->id);

			VkImageViewCreateInfo image_view_create_info = { VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO };
			image_view_create_info.image = texture->image;
			image_view_create_info.viewType = ToVkImageViewType(texture->type);
			image_view_create_info.format = texture->info.format;
			image_view_create_info.subresourceRange.aspectMask = DetermineAspectMask(texture->info.format);
			image_view_create_info.subresourceRange.baseMipLevel = 0;
			image_view_create_info.subresourceRange.levelCount = 1;
			image_view_create_info.subresourceRange.baseArrayLayer = 0;
			image_view_create_info.subresourceRange.layerCount = texture->info.arrayLayers;

			res = vkCreateImageView(device->device, &image_view_create_info, NULL, &texture->resolve_view);
			VK_CHECK_RESULT(res);
			VK_SET_OBJECT_NAME(device->device, texture->resolve_view, VK_OBJECT_TYPE_IMAGE_VIEW,
				"MGG_Texture.resolve_view (RenderTarget id: %llu)", texture->id);

		}

		texture->target_view = CreateImageView(device, texture, create_info.mipLevels);
		VK_SET_OBJECT_NAME(device->device, texture->target_view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.target_view (RenderTarget id: %llu)", texture->id);

		{
			std::lock_guard lock(device->transferMutex);

			VkCommandBuffer cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
			MGVK_CmdTransitionImageLayout(
				cmd,
				texture->image,
				VK_IMAGE_LAYOUT_UNDEFINED,
				startLayout,
				DetermineAspectMask(create_info.format),
				0,
				mipmaps,
				0,
				slices
			);

			MGVK_ExecuteAndFreeCommandBuffer(device, cmd, device->transferPool);
		}

		for (int i = 0; i < mipmaps; i++)
			texture->layouts[i] = startLayout;
		texture->optimal_layout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
	}

	if (depthFormat != MGDepthFormat::None)
	{
        texture->depthTexture = CreateDepthTexture(device, ToVkFormat(depthFormat), width, height, multiSampleCount);
		VK_SET_OBJECT_NAME(device->device, texture->depthTexture->image, VK_OBJECT_TYPE_IMAGE, "MGG_Texture.depthTexture.image (for RT id: %llu)", texture->id);
		texture->depthTexture->target_view = CreateImageView(device, texture->depthTexture, 1);
		VK_SET_OBJECT_NAME(device->device, texture->depthTexture->target_view, VK_OBJECT_TYPE_IMAGE_VIEW, "MGG_Texture.depthTexture.target_view (for RT id: %llu)", texture->id);
	}

	device->all_textures.push_back(texture);

	return texture;
}

void MGG_Texture_Destroy(MGG_GraphicsDevice* device, MGG_Texture* texture)
{
	assert(device != nullptr);
	assert(texture != nullptr);

	if (!texture)
		return;

	if (texture->depthTexture)
		MGG_Texture_Destroy(device, texture->depthTexture);

	// If this is an externally owned texture (like an external swapchain or image wrapped in a RenderTarget2D).
	if (texture->isExternal && texture->allocation == VK_NULL_HANDLE)
	{
		// Wait for the device to be idle to safely destroy views before the external caller destroys the underlying VkImage.
		vkDeviceWaitIdle(device->device);

		if (texture->isTarget)
		{
			MGVK_DestroyPipelines(device, [texture](const MGVK_PipelineState& s)
				{
					for (int i = 0; i < s.targets->set.numTargets; i++)
					{
						if (s.targets->set.targets[i] == texture)
						{
							return true;
						}
					}
					return false;
				});

			MGVK_DestroyTargetSets(device, [texture](const MGVK_TargetSetCache* s)
				{
					for (int i = 0; i < s->set.numTargets; i++)
					{
						if (s->set.targets[i] == texture)
						{
							return true;
						}
					}
					return false;
				});

			if (device->pipelineState.targets && device->pipelineState.targets->set.targets[0] == texture)
			{
				device->pipelineState.targets = nullptr;
			}
		}

		if (texture->target_view != VK_NULL_HANDLE)
		{
			vkDestroyImageView(device->device, texture->target_view, nullptr);
		}
		if (texture->view != VK_NULL_HANDLE)
		{
			vkDestroyImageView(device->device, texture->view, nullptr);
		}

		delete texture;
		return;
	}

	// Queue the texture for later destruction.
	device->destroyTextures.push(texture);
}

static void MGVK_ClampAndValidateTextureRegion(
	MGG_Texture* texture,
	mgint level,
	mgint slice,
	mgint& x, mgint& y, mgint& z,
	mgint& width, mgint& height, mgint& depth)
{
	assert(texture != nullptr);

	// Calculate the dimensions of the specified mipmap level.
	// Dimensions are halved at each level, with a minimum of 1.
	mgint mipWidth = getMipScalar(level, texture->info.extent.width);
	mgint mipHeight = getMipScalar(level, texture->info.extent.height);
	mgint mipDepth = getMipScalar(level, texture->info.extent.depth);

	// If width and height are 0, assume the user wants to operate on the whole mip level.
	if (width == 0 && height == 0)
	{
		width = mipWidth;
		height = mipHeight;
	}

	if (texture->info.imageType == VK_IMAGE_TYPE_2D)
	{
		depth = 1;
		z = 0;
		mipDepth = 1; // Correct mipDepth for assertions on 2D textures.
	}
	else
	{
		// If depth is 0 for a 3D texture, assume the whole mip depth.
		if (depth == 0)
		{
			depth = mipDepth;
		}
	}

	assert(level >= 0 && level < texture->info.mipLevels);
	assert(slice >= 0 && slice < texture->info.arrayLayers);
}

void MGG_Texture_SetData(MGG_GraphicsDevice* device, MGG_Texture* texture, mgint level, mgint slice, mgint x, mgint y, mgint z, mgint width, mgint height, mgint depth, mgbyte* data, mgint dataBytes)
{
	assert(device != nullptr);
	assert(texture != nullptr);

	MGVK_ClampAndValidateTextureRegion(texture, level, slice, x, y, z, width, height, depth);

	assert(data != nullptr);
	assert(dataBytes > 0);

	// TODO: Pool staging buffers or used discarded ones?

	MGG_Buffer buffer;
	MGVK_BufferCreate(device, dataBytes, VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VMA_MEMORY_USAGE_CPU_ONLY, &buffer);
	VK_SET_OBJECT_NAME(device->device, buffer.buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Texture_SetData::stagingBuffer");

	void* dest;
	vmaMapMemory(device->allocator, buffer.allocation, &dest);
	memcpy(dest, data, dataBytes);
	vmaUnmapMemory(device->allocator, buffer.allocation);

	// Etervale fork: on the render thread, while a frame records, the upload goes into the frame's own command buffer, so
	// it is ordered with the draws as in OpenGL: draws recorded before SetData read the old texels, draws after it the new
	// ones. Upstream submitted it at once on its own, ahead of the whole frame being recorded: every draw of that frame
	// read the last upload, so a texture updated twice in a frame (once per view) showed only the second content, and the
	// image depended on how far the previous frames had got on the GPU. Other threads keep the immediate submission.
	if (std::this_thread::get_id() == device->renderThread && device->frameIndex < device->frames.size()
		&& device->frames[device->frameIndex].is_recording)
	{
		auto& frame = device->frames[device->frameIndex];
		auto cmd = frame.commandBuffer;
		MGVK_EndRenderPass(device, cmd);
		device->renderTargetDirty = true;
		MGVK_CmdTransitionImageLayout(cmd, texture->image, texture->layouts[level], VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT, level, 1, slice, 1);
		MGVK_CmdCopyBufferToImage(cmd, buffer.buffer, texture->image, x, y, z, level, slice, width, height, depth);
		MGVK_CmdTransitionImageLayout(cmd, texture->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, texture->optimal_layout, VK_IMAGE_ASPECT_COLOR_BIT, level, 1, slice, 1);
		texture->layouts[level] = texture->optimal_layout;
		texture->uploadFrame = device->frame;
		device->frameStaging.push_back({ device->frame, buffer });
		return;
	}

	// Get a command buffer and fill it... only one thread at a time
	// can do this.  Eventually we could have multiple transfer pools.
	MGVK_Transfer transfer;
	VkCommandBuffer cmd;
	{
		std::lock_guard lock(device->transferMutex);
		cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);

		MGVK_CmdTransitionImageLayout(cmd, texture->image, texture->layouts[level], VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT, level, 1, slice, 1);
		texture->layouts[level] = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
		MGVK_CmdCopyBufferToImage(cmd, buffer.buffer, texture->image, x, y, z, level, slice, width, height, depth);
		MGVK_CmdTransitionImageLayout(cmd, texture->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, texture->optimal_layout, VK_IMAGE_ASPECT_COLOR_BIT, level, 1, slice, 1);
		texture->layouts[level] = texture->optimal_layout;

		transfer.owner = texture;
		transfer.cmd = cmd;
		transfer.staging = buffer;

		VkFenceCreateInfo fenceCreateInfo = {};
		fenceCreateInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
		fenceCreateInfo.flags = 0;
		vkCreateFence(device->device, &fenceCreateInfo, nullptr, &transfer.done);
		VK_SET_OBJECT_NAME(device->device, transfer.done, VK_OBJECT_TYPE_FENCE, "MGVK_Transfer.done");

		vkEndCommandBuffer(cmd);

		std::lock_guard lock2(device->queueMutex);
		VkSubmitInfo submit = {};
		submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
		submit.pNext = nullptr;
		submit.commandBufferCount = 1;
		submit.pCommandBuffers = &cmd;
		submit.signalSemaphoreCount = 0;
		vkQueueSubmit(device->queue, 1, &submit, transfer.done);

		device->pendingTransfers.push_back(transfer);
	}
}

void MGG_Texture_GetData(MGG_GraphicsDevice* device, MGG_Texture* texture, mgint level, mgint slice, mgint x, mgint y, mgint z, mgint width, mgint height, mgint depth, mgbyte* data, mgint dataBytes)
{
	assert(device != nullptr);
	assert(texture != nullptr);

	MGVK_ClampAndValidateTextureRegion(texture, level, slice, x, y, z, width, height, depth);

	assert(data != nullptr);
	assert(dataBytes > 0);

	bool restart_frame = false;

	std::lock_guard lock(device->transferMutex);
	MGVK_WaitPendingTransfers(device, texture);

	std::lock_guard lock2(device->queueMutex);

	auto& frame = device->frames[device->frameIndex];

	if (texture->isTarget)
	{
		// Etervale fork: also when the target was only written this frame (writeFrame). texture->frame marks sampling;
		// a target drawn into and read back without being sampled was copied before its commands were submitted,
		// and GetData returned the previous frame's pixels.
		if (frame.is_recording && (texture->frame == device->frame || texture->writeFrame == device->frame))
		{
			MGVK_FlushCommands(device, frame);
			restart_frame = true;
		}
	}
	else if (frame.is_recording && (texture->uploadFrame == device->frame || (texture->isStorage && texture->writeFrame == device->frame)))
	{
		// Etervale fork: its upload, or a dispatch that wrote it (a storage image), is in the frame's command buffer, not
		// submitted yet.
		MGVK_FlushCommands(device, frame);
		restart_frame = true;
	}

	MGG_Buffer buffer;
	MGVK_BufferCreate(device, dataBytes, VK_BUFFER_USAGE_TRANSFER_DST_BIT, VMA_MEMORY_USAGE_CPU_ONLY, &buffer);
	VK_SET_OBJECT_NAME(device->device, buffer.buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Texture_GetData::stagingBuffer");

	VkImageLayout originalLayout = texture->layouts[level];
	VkImageAspectFlags aspectMask = DetermineAspectMask(texture->info.format);

	VkCommandBuffer copyCmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
	MGVK_CmdTransitionImageLayout(copyCmd, texture->image, originalLayout, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, aspectMask, level, 1, slice, 1);
	MGVK_CmdCopyImageToBuffer(copyCmd, texture->image, buffer.buffer, x, y, z, level, slice, width, height, depth, aspectMask);
	MGVK_CmdTransitionImageLayout(copyCmd, texture->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, originalLayout, aspectMask, level, 1, slice, 1);
	MGVK_ExecuteAndFreeCommandBuffer(device, copyCmd, device->transferPool);

	void* src;
	vmaMapMemory(device->allocator, buffer.allocation, &src);
	memcpy(data, src, dataBytes);
	vmaUnmapMemory(device->allocator, buffer.allocation);

	vmaDestroyBuffer(device->allocator, buffer.buffer, buffer.allocation);

	if (restart_frame)
		MGVK_BeginCommandBuffer(frame.commandBuffer);
}

static bool MGVK_SemanticUsage(std::string name, MGVertexElementUsage& usage)
{
	for (auto& c : name)
		c = (char)toupper((unsigned char)c);

	// The names mgfxc maps (ShaderProfile.Vulkan.cs); anything else is a texture coordinate there too.
	static const std::pair<const char*, MGVertexElementUsage> table[] =
	{
		{ "POSITION", MGVertexElementUsage::Position },
		{ "SV_POSITION", MGVertexElementUsage::Position },
		{ "COLOR", MGVertexElementUsage::Color },
		{ "TEXCOORD", MGVertexElementUsage::TextureCoordinate },
		{ "NORMAL", MGVertexElementUsage::Normal },
		{ "BINORMAL", MGVertexElementUsage::Binormal },
		{ "TANGENT", MGVertexElementUsage::Tangent },
		{ "BLENDINDICES", MGVertexElementUsage::BlendIndices },
		{ "BLENDWEIGHT", MGVertexElementUsage::BlendWeight },
		{ "DEPTH", MGVertexElementUsage::Depth },
		{ "FOG", MGVertexElementUsage::Fog },
		{ "POINTSIZE", MGVertexElementUsage::PointSize },
		{ "TESSELLATEFACTOR", MGVertexElementUsage::TessellateFactor },
	};
	for (auto& entry : table)
	{
		if (name == entry.first)
		{
			usage = entry.second;
			return true;
		}
	}
	usage = MGVertexElementUsage::TextureCoordinate;
	return false;
}

// Reads the Location of every vertex input from the SPIR-V, keyed by its HLSL semantic (the UserSemantic decoration,
// else DXC's "in.var.<SEMANTIC>" name).
//
// The C# side builds the input elements in the order of the inputs mgfxc reflected, and those are only the inputs the
// shader reads. DXC drops an unread input from the entry point but keeps the locations of the others, so the i-th
// reflected input is not always at location i (an unread NORMAL0 between POSITION0 and TEXCOORD0 leaves TEXCOORD0 at
// location 2 as the second element). Matching by semantic gives each element its real location.
static void MGVK_ReflectInputLocations(const uint32_t* code, size_t words, std::vector<MGVK_InputLocation>& result)
{
	if (words < 5 || code[0] != 0x07230203)
		return;

	std::map<uint32_t, std::string> names, semantics;
	std::map<uint32_t, uint32_t> locations;
	std::vector<uint32_t> inputs;

	for (size_t i = 5; i < words;)
	{
		const uint32_t count = code[i] >> 16;
		const uint32_t op = code[i] & 0xFFFF;
		if (count == 0 || i + count > words)
			break;
		const uint32_t* ops = code + i + 1;
		auto literal = [&](uint32_t first)
		{
			const char* text = (const char*)(ops + first);
			return std::string(text, strnlen(text, (count - 1 - first) * 4));
		};

		if (op == 54)	// OpFunction: the declarations are over
			break;
		if (op == 5 && count > 2)	// OpName
			names[ops[0]] = literal(1);
		else if (op == 71 && count > 3 && ops[1] == 30)	// OpDecorate Location
			locations[ops[0]] = ops[2];
		else if (op == 5633 && count > 3 && ops[1] == 5635)	// OpDecorateString UserSemantic
			semantics[ops[0]] = literal(2);
		else if (op == 59 && count > 3 && ops[2] == 1)	// OpVariable Input
			inputs.push_back(ops[1]);

		i += count;
	}

	for (auto id : inputs)
	{
		auto location = locations.find(id);
		if (location == locations.end())
			continue;	// a built-in

		std::string semantic;
		auto user = semantics.find(id);
		auto name = names.find(id);
		if (user != semantics.end())
			semantic = user->second;
		else if (name != names.end() && name->second.rfind("in.var.", 0) == 0)
			semantic = name->second.substr(7);
		else
			continue;

		size_t digits = semantic.size();
		while (digits > 0 && isdigit((unsigned char)semantic[digits - 1]))
			digits--;

		MGVK_InputLocation input;
		input.index = digits < semantic.size() ? (mguint)strtoul(semantic.c_str() + digits, nullptr, 10) : 0;
		MGVK_SemanticUsage(semantic.substr(0, digits), input.usage);
		input.location = location->second;
		result.push_back(input);
	}
}

// The location of the vertex element (usage, index): the input with that usage whose first semantic index is the
// nearest at or below it (a matrix or array input spans consecutive locations).
static bool MGVK_FindInputLocation(const MGG_Shader* shader, MGVertexElementUsage usage, mguint index, uint32_t& location)
{
	const MGVK_InputLocation* best = nullptr;
	for (auto& input : shader->inputLocations)
	{
		if (input.usage == usage && input.index <= index && (best == nullptr || input.index > best->index))
			best = &input;
	}
	if (best == nullptr)
		return false;
	location = best->location + (index - best->index);
	return true;
}

MGG_InputLayout* MGG_InputLayout_Create(
	MGG_GraphicsDevice* device,
	MGG_Shader* vertexShader,
	mgint* strides,
	mgint streamCount,
	MGG_InputElement* elements,
	mgint elementCount
	)
{
	assert(device != nullptr);
	assert(streamCount >= 0);
	assert(strides != nullptr);
	assert(elements != nullptr);
	assert(elementCount >= 0);

	auto layout = new MGG_InputLayout();
	layout->uid = device->nextUid++;

	layout->bindings.resize(streamCount);
	for (int i = 0; i < streamCount; i++)
	{
		auto& binding = layout->bindings[i];
		binding.binding = i;
		binding.stride = strides[i];
		binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;
	}

	layout->attributes.resize(elementCount);
	for (int i = 0; i < elementCount; i++)
	{
		const auto element = elements[i];
		auto& attrib = layout->attributes[i];

		// By semantic: the element order follows the reflected inputs, not their locations (MGVK_ReflectInputLocations).
		uint32_t location;
		if (vertexShader == nullptr || !MGVK_FindInputLocation(vertexShader, element.SemanticUsage, element.SemanticIndex, location))
			location = i;
		attrib.location = location;
		attrib.binding = element.VertexBufferSlot;
		attrib.format = ToVkFormat(element.Format);
		attrib.offset = element.AlignedByteOffset;

		if (element.InstanceDataStepRate > 0)
		{
			// Override input rate for instanced elements.
			layout->bindings[element.VertexBufferSlot].inputRate = VK_VERTEX_INPUT_RATE_INSTANCE;
		}
	}

	return layout;
}

void MGG_InputLayout_Destroy(MGG_GraphicsDevice* device, MGG_InputLayout* layout)
{
	assert(device != nullptr);
	assert(layout != nullptr);

	if (layout == nullptr)
		return;

	// Etervale fork: not the current layout any more (its pipelines stay keyed by its id, which is never reused).
	if (device->pipelineState.layout == layout)
	{
		device->pipelineState.layout = nullptr;
		device->pipelineStateDirty = true;
	}
	delete layout;
}

// Etervale fork (issue 005): a compute shader's pipeline and its layout (one descriptor set, the shader's own). Unlike a
// graphics pipeline it depends on no other state, so it is built once with the shader.
static void MGVK_CreateComputePipeline(MGG_GraphicsDevice* device, MGG_Shader* shader)
{
	if (shader->stage != MGShaderStage::Compute)
		return;

	VkPipelineLayoutCreateInfo layoutInfo = { VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO };
	layoutInfo.setLayoutCount = 1;
	layoutInfo.pSetLayouts = &shader->setLayout;
	VkResult res = vkCreatePipelineLayout(device->device, &layoutInfo, nullptr, &shader->computeLayout);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, shader->computeLayout, VK_OBJECT_TYPE_PIPELINE_LAYOUT, "MGG_Shader.computeLayout (id: %u)", shader->id);

	VkComputePipelineCreateInfo pipelineInfo = { VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO };
	pipelineInfo.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	pipelineInfo.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
	pipelineInfo.stage.module = shader->module;
	pipelineInfo.stage.pName = "main";
	pipelineInfo.layout = shader->computeLayout;
	if (device->pipelineStatistics)
		pipelineInfo.flags |= VK_PIPELINE_CREATE_CAPTURE_STATISTICS_BIT_KHR;   // Etervale fork, issue 012
	res = vkCreateComputePipelines(device->device, device->pipelineCache, 1, &pipelineInfo, nullptr, &shader->computePipeline);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, shader->computePipeline, VK_OBJECT_TYPE_PIPELINE, "MGG_Shader.computePipeline (id: %u)", shader->id);
}

MGG_Shader* MGG_Shader_Create(MGG_GraphicsDevice* device, MGShaderStage stage, mgbyte* bytecode, mgint sizeInBytes)
{
	assert(device != nullptr);
	assert(bytecode != nullptr);
	assert(sizeInBytes > 0);

	auto shader = new MGG_Shader();
	shader->stage = stage;

	// We store the pre-generated bindings info at the top of the shader bytecode data.
	int uniformCount = 0;
	auto& layoutBindings = shader->bindings;
	{
		uniformCount = *(mgint*)bytecode; bytecode += sizeof(mgint); sizeInBytes -= sizeof(mgint);
		shader->uniformSlots = *(mguint*)bytecode; bytecode += sizeof(mguint); sizeInBytes -= sizeof(mguint);
		shader->textureSlots = *(mguint*)bytecode; bytecode += sizeof(mguint); sizeInBytes -= sizeof(mguint);
		shader->samplerSlots = *(mguint*)bytecode; bytecode += sizeof(mguint); sizeInBytes -= sizeof(mguint);

		memcpy(shader->textureTypes, bytecode, sizeof(MGTextureType) * MAX_TEXTURE_SLOTS);
		bytecode += sizeof(MGTextureType) * MAX_TEXTURE_SLOTS;
		sizeInBytes -= sizeof(MGTextureType) * MAX_TEXTURE_SLOTS;

		int count = *(mgint*)bytecode; bytecode += sizeof(mgint); sizeInBytes -= sizeof(mgint);
		layoutBindings.resize(count);
		memcpy(layoutBindings.data(), bytecode, sizeof(VkDescriptorSetLayoutBinding) * count);

		// The shader bytecode follows.
		bytecode += sizeof(VkDescriptorSetLayoutBinding) * count;
		sizeInBytes -= sizeof(VkDescriptorSetLayoutBinding) * count;
	}

	if (stage == MGShaderStage::Vertex)
		MGVK_ReflectInputLocations((const uint32_t*)bytecode, sizeInBytes / 4, shader->inputLocations);

	// Etervale fork: the storage resources of the shader, by register (issues 005, 006).
	for (auto& b : layoutBindings)
	{
		if (b.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER && b.binding >= MGVK_TEXTURE_BINDING && b.binding < MGVK_TEXTURE_BINDING + MAX_TEXTURE_SLOTS)
			shader->bufferSlots |= 1u << (b.binding - MGVK_TEXTURE_BINDING);
		else if (b.binding >= MGVK_UAV_BINDING && b.binding < MGVK_UAV_BINDING + MGVK_MAX_UAV_SLOTS)
		{
			shader->uavSlots |= 1u << (b.binding - MGVK_UAV_BINDING);
			if (b.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE)
				shader->uavImageSlots |= 1u << (b.binding - MGVK_UAV_BINDING);
		}
	}

	VkShaderModuleCreateInfo create_info = { VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO };
	create_info.codeSize = sizeInBytes;
	create_info.pCode = (uint32_t*)bytecode;
	VkResult res = vkCreateShaderModule(device->device, &create_info, NULL, &shader->module);
	VK_CHECK_RESULT(res);

	shader->id = ++device->currentShaderId;
	VK_SET_OBJECT_NAME(device->device, shader->module, VK_OBJECT_TYPE_SHADER_MODULE, "MGG_Shader.module (id: %u)", shader->id);

	device->all_shaders.push_back(shader);

	// If the shader has no bindings we still need an empty layout
	// to ensure that the descriptor sets are in the right slots.
	if (layoutBindings.empty())
	{
		VkDescriptorSetLayoutCreateInfo layoutInfo;
		memset(&layoutInfo, 0, sizeof(layoutInfo));
		layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
		layoutInfo.bindingCount = 0;
		layoutInfo.pBindings = nullptr;
		layoutInfo.flags = 0;
		res = vkCreateDescriptorSetLayout(device->device, &layoutInfo, nullptr, &shader->setLayout);
		VK_CHECK_RESULT(res);

		shader->poolInfo = nullptr;
		shader->pool = nullptr;
		shader->writes = nullptr;
		MGVK_CreateComputePipeline(device, shader);
		return shader;
	}

	// Etervale fork: two resources of one shader on the same register give a layout with a duplicated binding
	// (VUID-VkDescriptorSetLayoutCreateInfo-binding-00279): only one of them is bound. Said once per shader.
	for (size_t a = 0; a < layoutBindings.size(); a++)
		for (size_t b = a + 1; b < layoutBindings.size(); b++)
			if (layoutBindings[a].binding == layoutBindings[b].binding)
				printf("Shader %u (stage %d): two resources share binding %u; give them separate registers\n",
					shader->id, (int)stage, layoutBindings[a].binding);

	VkDescriptorSetLayoutCreateInfo layoutInfo;
	memset(&layoutInfo, 0, sizeof(layoutInfo));
	layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
	layoutInfo.bindingCount = layoutBindings.size();
	layoutInfo.pBindings = layoutBindings.data();
	layoutInfo.flags = 0;

	res = vkCreateDescriptorSetLayout(device->device, &layoutInfo, nullptr, &shader->setLayout);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, shader->setLayout, VK_OBJECT_TYPE_DESCRIPTOR_SET_LAYOUT, "MGG_Shader.setLayout (id: %u)", shader->id);

	// Prepare the initial descriptor pool.
	{
		VkDescriptorPoolSize* pool_sizes = new VkDescriptorPoolSize[layoutBindings.size()];

		for (int i = 0; i < layoutBindings.size(); i++)
		{
			auto& b = layoutBindings[i];
			pool_sizes[i].type = b.descriptorType;
			pool_sizes[i].descriptorCount = DefaultPoolSize;
		}

		shader->poolInfo = new VkDescriptorPoolCreateInfo();
		shader->poolInfo->sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
		shader->poolInfo->flags = 0;
		shader->poolInfo->maxSets = DefaultPoolSize;
		shader->poolInfo->poolSizeCount = layoutBindings.size();
		shader->poolInfo->pPoolSizes = pool_sizes;

		res = vkCreateDescriptorPool(device->device, shader->poolInfo, nullptr, &shader->pool);
		VK_CHECK_RESULT(res);
		VK_SET_OBJECT_NAME(device->device, shader->pool, VK_OBJECT_TYPE_DESCRIPTOR_POOL, "MGG_Shader.pool (id: %u)", shader->id);
	}

	// Pre-fill the free descriptor sets now.
	MGVK_FillDescriptorSetCache(device, shader);

	// Prepare the write descriptor set for updates at runtime. (Etervale fork: storage buffers take a buffer info too.)
	size_t bufferBindings = 0;
	for (auto& b : layoutBindings)
	{
		if (b.descriptorType == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC || b.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)
			bufferBindings++;
	}
	VkWriteDescriptorSet* write = shader->writes = new VkWriteDescriptorSet[layoutBindings.size()];
	VkDescriptorBufferInfo* bufferInfo = shader->bufferInfo = new VkDescriptorBufferInfo[bufferBindings];
	VkDescriptorImageInfo* imageInfo = shader->imageInfo =  new VkDescriptorImageInfo[layoutBindings.size() - bufferBindings];
	for (int i = 0; i < layoutBindings.size(); i++)
	{
		auto& b = layoutBindings[i];

		if (b.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)
		{
			// Etervale fork: a StructuredBuffer/ByteAddressBuffer (t) or their RW forms (u); the buffer is set per draw.
			bufferInfo->buffer = VK_NULL_HANDLE;
			bufferInfo->offset = 0;
			bufferInfo->range = VK_WHOLE_SIZE;

			write->sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
			write->pNext = nullptr;
			write->dstSet = nullptr;
			write->dstBinding = b.binding;
			write->dstArrayElement = 0;
			write->descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
			write->descriptorCount = 1;
			write->pImageInfo = nullptr;
			write->pBufferInfo = bufferInfo++;
			write->pTexelBufferView = nullptr;

			write++;
		}
		else if (b.descriptorType == VK_DESCRIPTOR_TYPE_STORAGE_IMAGE)
		{
			// Etervale fork: a RWTexture2D, written in the GENERAL layout during the dispatch.
			imageInfo->imageLayout = VK_IMAGE_LAYOUT_GENERAL;
			imageInfo->imageView = nullptr;
			imageInfo->sampler = nullptr;

			write->sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
			write->pNext = nullptr;
			write->dstSet = nullptr;
			write->dstBinding = b.binding;
			write->dstArrayElement = 0;
			write->descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
			write->descriptorCount = 1;
			write->pImageInfo = imageInfo++;
			write->pBufferInfo = nullptr;
			write->pTexelBufferView = nullptr;

			write++;
		}
		else if (b.descriptorType == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC)
		{
			bufferInfo->buffer = NULL;
			bufferInfo->offset = 0;
			bufferInfo->range = VK_WHOLE_SIZE;

			write->sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
			write->pNext = nullptr;
			write->dstSet = nullptr;
			write->dstBinding = b.binding;
			write->dstArrayElement = 0;
			write->descriptorType = b.descriptorType;
			write->descriptorCount = 1;
			write->pImageInfo = nullptr;
			write->pBufferInfo = bufferInfo++;
			write->pTexelBufferView = nullptr;

			write++;
		}
		else if (b.descriptorType == VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER)
		{
			imageInfo->imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
			imageInfo->imageView = nullptr;
			imageInfo->sampler = nullptr;

			write->sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
			write->pNext = nullptr;
			write->dstSet = nullptr;
			write->dstBinding = b.binding;
			write->dstArrayElement = 0;
			write->descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
			write->descriptorCount = 1;
			write->pImageInfo = imageInfo++;
			write->pBufferInfo = nullptr;
			write->pTexelBufferView = nullptr;

			write++;
		}
		else if (b.descriptorType == VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE)
		{
			imageInfo->imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
			imageInfo->imageView = nullptr;
			imageInfo->sampler = nullptr;

			write->sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
			write->pNext = nullptr;
			write->dstSet = nullptr;
			write->dstBinding = b.binding;
			write->dstArrayElement = 0;
			write->descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
			write->descriptorCount = 1;
			write->pImageInfo = imageInfo++;
			write->pBufferInfo = nullptr;
			write->pTexelBufferView = nullptr;

			write++;
		}
		else
		{
			assert(0);
		}
	}

	MGVK_CreateComputePipeline(device, shader);
	return shader;
}

void MGG_Shader_Destroy(MGG_GraphicsDevice* device, MGG_Shader* shader)
{
	assert(device != nullptr);
	assert(shader != nullptr);

	if (!shader)
		return;

	// Etervale fork: a compute shader's pipeline may still run in a frame in flight; effects are disposed rarely, so the
	// device is waited once rather than the pipeline queued for later destruction.
	if (shader->computePipeline != VK_NULL_HANDLE)
	{
		vkDeviceWaitIdle(device->device);
		vkDestroyPipeline(device->device, shader->computePipeline, nullptr);
		vkDestroyPipelineLayout(device->device, shader->computeLayout, nullptr);
		if (device->shaders[(mgint)MGShaderStage::Compute] == shader)
			device->shaders[(mgint)MGShaderStage::Compute] = nullptr;
	}

	delete [] shader->bufferInfo;
	delete [] shader->imageInfo;
	delete [] shader->writes;

	for (auto pair : shader->usedSets)
	{
		//vkFreeDescriptorSets(device->device, shader->pool, 1, &pair.second->set);
		delete pair.second;
	}

	while (shader->freeSets.size() > 0)
	{
		auto free = shader->freeSets.front();
		shader->freeSets.pop();

		//vkFreeDescriptorSets(device->device, shader->pool, 1, &free->set);
		delete free;
	}

	vkDestroyDescriptorSetLayout(device->device, shader->setLayout, nullptr);

	vkDestroyDescriptorPool(device->device, shader->pool, nullptr);
	if (shader->poolInfo)
	{
		delete [] shader->poolInfo->pPoolSizes;
		delete shader->poolInfo;
	}

	vkDestroyShaderModule(device->device, shader->module, nullptr);

	mg_remove(device->all_shaders, shader);
	delete shader;
}

MGG_OcclusionQuery* MGG_OcclusionQuery_Create(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);

	auto query = new MGG_OcclusionQuery();

	VkQueryPoolCreateInfo createInfo = { VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO };
	createInfo.queryType = VK_QUERY_TYPE_OCCLUSION;
	createInfo.queryCount = 1;
	query->gpuHasBegun = false;

	VkResult res = vkCreateQueryPool(device->device, &createInfo, nullptr, &query->queryPool);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, query->queryPool, VK_OBJECT_TYPE_QUERY_POOL, "MGG_OcclusionQuery.queryPool");

	return query;
}

void MGG_OcclusionQuery_Destroy(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
	assert(device != nullptr);
	assert(query != nullptr);

	if (!query)
		return;

	if (query->queryPool != VK_NULL_HANDLE)
	{
		vkDeviceWaitIdle(device->device);
		vkDestroyQueryPool(device->device, query->queryPool, nullptr);
	}

	delete query;
}

void MGG_OcclusionQuery_Begin(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
	assert(device != nullptr);
	assert(query != nullptr);
	assert(!query->inBeginEndBlock);

	device->deferredOcclusionQueries.push_back(query);

	query->inBeginEndBlock = true;
	query->isComplete = false;
	query->pixelCount = 0;

	device->renderTargetDirty = true;
}

void MGG_OcclusionQuery_End(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
	assert(device != nullptr);
	assert(query != nullptr);
	assert(query->inBeginEndBlock);

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

    if (query->gpuHasBegun)
    {
        vkCmdEndQuery(frame.commandBuffer, query->queryPool, 0);
		query->gpuHasBegun = false;
    }
    query->inBeginEndBlock = false;

    MGVK_EndRenderPass(device, frame.commandBuffer);
    VK_CHECK_RESULT(vkEndCommandBuffer(frame.commandBuffer));

    VkFence fence;
    VkFenceCreateInfo fenceInfo = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
    VK_CHECK_RESULT(vkCreateFence(device->device, &fenceInfo, nullptr, &fence));

    VkSubmitInfo submitInfo = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &frame.commandBuffer;
	{
		std::lock_guard lock(device->queueMutex);
		VK_CHECK_RESULT(vkQueueSubmit(device->queue, 1, &submitInfo, fence));
	}

    VK_CHECK_RESULT(vkWaitForFences(device->device, 1, &fence, VK_TRUE, UINT64_MAX));
    vkDestroyFence(device->device, fence, nullptr);

	MGVK_BeginCommandBuffer(frame.commandBuffer);
    device->renderTargetDirty = true;
}

mgbyte MGG_OcclusionQuery_GetResult(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query, mgint& pixelCount)
{
	assert(device != nullptr);
	assert(query != nullptr);
	assert(!query->inBeginEndBlock);

	// If the result is already available from a previous check, return it immediately.
	if (query->isComplete)
	{
		pixelCount = query->pixelCount;
		return true;
	}

	uint64_t result = 0;

	// Poll for the result without waiting. This prevents stalling the CPU.
	// VK_QUERY_RESULT_64_BIT requests a 64-bit integer result.
	VkResult res = vkGetQueryPoolResults(
		device->device,
		query->queryPool,
		0, // firstQuery
		1, // queryCount
		sizeof(uint64_t), // dataSize
		&result,          // pData
		sizeof(uint64_t), // stride
		VK_QUERY_RESULT_64_BIT
	);

	if (res == VK_SUCCESS)
	{
		query->pixelCount = (mgint)result;
		pixelCount = query->pixelCount;
		query->isComplete = true;
		return true;
	}
	else if (res == VK_NOT_READY)
	{
		// The GPU has not finished processing the query yet.
		pixelCount = 0;
		return false;
	}
	else
	{
		// An error occurred.
		VK_CHECK_RESULT(res);
		pixelCount = 0;
		return false; // Return false indicating the result is not available.
	}
}

MGG_Texture* MGG_RenderTarget_WrapNativeHandle(
	MGG_GraphicsDevice* device,
	void* nativeHandle,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	MGDepthFormat depthFormat,
	mgint multiSampleCount,
	mgbyte externalPresentation)
{
	assert(device != nullptr);
	assert(nativeHandle != nullptr);
	assert(width > 0);
	assert(height > 0);

	auto texture = new MGG_Texture();
	texture->isTarget = true;
	texture->isExternal = true; // Marks as externally owned. Prevents image/memory destruction.
	texture->type = MGTextureType::_2D;
	texture->format = format;
	texture->id = ++device->currentTextureId;
	texture->multiSampleCount = multiSampleCount;
	texture->usage = MGRenderTargetUsage::DiscardContents;

	// Set the externally-owned VkImage. We don't allocate memory.
	texture->image = static_cast<VkImage>(nativeHandle);
	texture->allocation = VK_NULL_HANDLE; // We don't own the image. Prevents us from trying to free image memory.

	// Populate VkImageCreateInfo for view creation.
	VkImageCreateInfo& create_info = texture->info;
	create_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	create_info.imageType = ToVkImageType(texture->type);
	create_info.flags = ToVkImageCreateFlags(texture->type);
	create_info.format = ToVkFormat(format);
	create_info.extent.width = width;
	create_info.extent.height = height;
	create_info.extent.depth = 1;
	create_info.mipLevels = 1;
	create_info.arrayLayers = 1;
	create_info.samples = ToVkSampleCount(multiSampleCount);
	create_info.tiling = VK_IMAGE_TILING_OPTIMAL;
	create_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
	create_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

	// External presentation surfaces (OpenXR swapchains, etc.) should have COLOR_ATTACHMENT_OPTIMAL.
	// Otherwise, set layout for normal shader sampling, as in standard RenderTarget2D.
	VkImageLayout layout = externalPresentation
		? VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
		: VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

	texture->layouts[0] = layout;
	texture->optimal_layout = layout;

	// Create image view for shader sampling.
	texture->view = CreateImageView(device, texture, 1);
	VK_SET_OBJECT_NAME(device->device,
		texture->view,
		VK_OBJECT_TYPE_IMAGE_VIEW,
		"MGG_Texture.view (External RenderTarget id: %llu)",
		texture->id);

	// Create image view for render target.
	texture->target_view = CreateImageView(device, texture, 1);
	VK_SET_OBJECT_NAME(device->device,
		texture->target_view,
		VK_OBJECT_TYPE_IMAGE_VIEW,
		"MGG_Texture.target_view (External RenderTarget id: %llu)",
		texture->id);

	// Create depth buffer if requested (owned by this texture wrapper).
	if (depthFormat != MGDepthFormat::None)
	{
		texture->depthFormat = depthFormat;
		texture->depthTexture = CreateDepthTexture(device, ToVkFormat(depthFormat), width, height, multiSampleCount);

		VK_SET_OBJECT_NAME(device->device,
			texture->depthTexture->image,
			VK_OBJECT_TYPE_IMAGE,
			"MGG_Texture.depthTexture (External RenderTarget id: %llu)",
			texture->id);

		texture->depthTexture->target_view = CreateImageView(device,
			texture->depthTexture,
			1);

		VK_SET_OBJECT_NAME(device->device,
			texture->depthTexture->target_view,
			VK_OBJECT_TYPE_IMAGE_VIEW,
			"MGG_Texture.depthTexture.target_view (External RenderTarget id: %llu)",
			texture->id);
	}

	return texture;
}

MG_EXPORT void MGG_GraphicsDevice_GetNativeHandles(const MGG_GraphicsDevice* device, MGP_NativeGraphicsHandles* handles)
{
	if (!device || !handles)
	{
		return;
	}

	handles->Backend          = MGGraphicsBackend::Vulkan;
	handles->Instance         = device->instance;
	handles->PhysicalDevice   = device->physicalDevice;
	handles->LogicalDevice    = device->device;
	handles->Queue            = static_cast<void*>(device->queue);
	handles->QueueFamilyIndex = static_cast<mgint>(device->graphicsQueueFamily);
	handles->QueueIndex       = 0;
}


// Etervale fork: the version of the runtime's exported interface, which the fork's framework (monogame/framework,
// MGG.EnsureRuntimeVersion) checks before anything else: a library built from other sources (the official package, or
// a stale build of the fork) would fail later at the first missing export, or worse, call an export whose arguments
// changed. Bumped with every change of an exported function (monogame/issues/002-runtime-version.md).
#define MGVK_ETERVALE_RUNTIME_VERSION 7

MG_EXPORT mgint MGG_Etervale_RuntimeVersion()
{
	return MGVK_ETERVALE_RUNTIME_VERSION;
}


// Etervale fork (issue 012): the driver's statistics of the last bound graphics (compute = 0) or compute (compute = 1)
// pipeline (VK_KHR_pipeline_executable_properties), as UTF-8 text, one line per statistic of each executable (a stage
// of the pipeline): "executable<TAB>statistic<TAB>value<LF>". Written into `buffer` (at most `capacity` bytes, NUL
// terminated); returns the length written, 0 when no pipeline has been bound yet, or -1 when the statistics are off
// (MG_PIPELINE_STATISTICS=1 was not set at device creation, or the driver lacks the extension).
MG_EXPORT mgint MGG_GraphicsDevice_GetPipelineStatistics(MGG_GraphicsDevice* device, mgint compute, char* buffer, mgint capacity)
{
	assert(device != nullptr);
	if (!device->pipelineStatistics)
		return -1;
	VkPipeline pipeline = compute ? device->lastComputePipeline : device->lastGraphicsPipeline;
	if (pipeline == VK_NULL_HANDLE || buffer == nullptr || capacity <= 0)
		return 0;

	VkPipelineInfoKHR info = { VK_STRUCTURE_TYPE_PIPELINE_INFO_KHR };
	info.pipeline = pipeline;
	uint32_t executables = 0;
	vkGetPipelineExecutablePropertiesKHR(device->device, &info, &executables, nullptr);
	std::vector<VkPipelineExecutablePropertiesKHR> properties(executables, { VK_STRUCTURE_TYPE_PIPELINE_EXECUTABLE_PROPERTIES_KHR });
	vkGetPipelineExecutablePropertiesKHR(device->device, &info, &executables, properties.data());

	std::string text;
	for (uint32_t e = 0; e < executables; e++)
	{
		VkPipelineExecutableInfoKHR executable = { VK_STRUCTURE_TYPE_PIPELINE_EXECUTABLE_INFO_KHR };
		executable.pipeline = pipeline;
		executable.executableIndex = e;
		uint32_t count = 0;
		vkGetPipelineExecutableStatisticsKHR(device->device, &executable, &count, nullptr);
		std::vector<VkPipelineExecutableStatisticKHR> statistics(count, { VK_STRUCTURE_TYPE_PIPELINE_EXECUTABLE_STATISTIC_KHR });
		vkGetPipelineExecutableStatisticsKHR(device->device, &executable, &count, statistics.data());
		for (const auto& statistic : statistics)
		{
			char value[64];
			switch (statistic.format)
			{
			case VK_PIPELINE_EXECUTABLE_STATISTIC_FORMAT_BOOL32_KHR:
				snprintf(value, sizeof(value), "%s", statistic.value.b32 ? "true" : "false");
				break;
			case VK_PIPELINE_EXECUTABLE_STATISTIC_FORMAT_INT64_KHR:
				snprintf(value, sizeof(value), "%lld", (long long)statistic.value.i64);
				break;
			case VK_PIPELINE_EXECUTABLE_STATISTIC_FORMAT_UINT64_KHR:
				snprintf(value, sizeof(value), "%llu", (unsigned long long)statistic.value.u64);
				break;
			case VK_PIPELINE_EXECUTABLE_STATISTIC_FORMAT_FLOAT64_KHR:
				snprintf(value, sizeof(value), "%g", statistic.value.f64);
				break;
			default:
				snprintf(value, sizeof(value), "?");
				break;
			}
			text += properties[e].name;
			text += '\t';
			text += statistic.name;
			text += '\t';
			text += value;
			text += '\n';
		}
	}

	mgint length = (mgint)std::min<size_t>(text.size(), (size_t)(capacity - 1));
	memcpy(buffer, text.data(), (size_t)length);
	buffer[length] = 0;
	return length;
}


// GPU timestamp queries recorded in the frame's command buffer, for per-pass GPU timing.
// A pool holds `count` 64-bit timestamps. Reset a range at the start of a frame (outside any render pass), write
// timestamps between draws, and read them back a few frames later without waiting.

MG_EXPORT void* MGG_TimestampPool_Create(MGG_GraphicsDevice* device, mguint count)
{
	assert(device != nullptr);
	VkQueryPoolCreateInfo info = { VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO };
	info.queryType = VK_QUERY_TYPE_TIMESTAMP;
	info.queryCount = count;
	VkQueryPool pool = VK_NULL_HANDLE;
	if (vkCreateQueryPool(device->device, &info, nullptr, &pool) != VK_SUCCESS)
		return nullptr;
	return (void*)pool;
}

MG_EXPORT void MGG_TimestampPool_Destroy(MGG_GraphicsDevice* device, void* pool)
{
	assert(device != nullptr);
	if (pool == nullptr)
		return;
	vkDeviceWaitIdle(device->device);
	vkDestroyQueryPool(device->device, (VkQueryPool)pool, nullptr);
}

MG_EXPORT mgbool MGG_TimestampPool_Reset(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count)
{
	assert(device != nullptr);
	auto& frame = device->frames[device->frameIndex];
	// vkCmdResetQueryPool is not allowed inside a render pass, and ending one here would change its load/store.
	if (!frame.is_recording || device->inRenderPass)
		return false;
	vkCmdResetQueryPool(frame.commandBuffer, (VkQueryPool)pool, first, count);
	return true;
}

MG_EXPORT void MGG_TimestampPool_Write(MGG_GraphicsDevice* device, void* pool, mguint index)
{
	assert(device != nullptr);
	auto& frame = device->frames[device->frameIndex];
	if (!frame.is_recording)
		return;
	vkCmdWriteTimestamp(frame.commandBuffer, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, (VkQueryPool)pool, index);
}

MG_EXPORT mgbool MGG_TimestampPool_Read(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count, mgulong* ticks)
{
	assert(device != nullptr);
	VkResult result = vkGetQueryPoolResults(device->device, (VkQueryPool)pool, first, count,
		count * sizeof(mgulong), ticks, sizeof(mgulong), VK_QUERY_RESULT_64_BIT);
	return result == VK_SUCCESS;
}

MG_EXPORT mgfloat MGG_TimestampPool_GetPeriod(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);
	return device->deviceProperties.limits.timestampPeriod;
}


// Etervale fork: asynchronous frame capture. MGG_FrameCapture_Start makes every Present copy the image it presents,
// downscaled by an integer factor, into a host buffer of its frame slot (commands recorded in the frame's own command
// buffer). When the slot's fence is waited before the slot is reused (MGVK_TryAcquireSwap), the copy is moved to a
// queue that MGG_FrameCapture_Pop drains. Nothing waits for the GPU beyond what the runtime already waits for, so the
// frames in flight overlap exactly as without capturing: synchronisation hazards and races stay visible.

static void MGVK_ReleaseCaptureSlot(MGG_GraphicsDevice* device, MGVK_CaptureSlot& slot)
{
	if (slot.buffer != VK_NULL_HANDLE)
		vmaDestroyBuffer(device->allocator, slot.buffer, slot.bufferAllocation);
	if (slot.image != VK_NULL_HANDLE)
		vmaDestroyImage(device->allocator, slot.image, slot.imageAllocation);
	slot = MGVK_CaptureSlot();
}

static bool MGVK_EnsureCaptureSlot(MGG_GraphicsDevice* device, MGVK_CaptureSlot& slot, uint32_t width, uint32_t height)
{
	if (slot.image != VK_NULL_HANDLE && slot.width == width && slot.height == height)
		return true;
	// The slot's previous copy belongs to a frame whose fence has been waited: nothing on the GPU uses it any more.
	MGVK_ReleaseCaptureSlot(device, slot);

	VkImageCreateInfo imageInfo = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
	imageInfo.imageType = VK_IMAGE_TYPE_2D;
	imageInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
	imageInfo.extent = { width, height, 1 };
	imageInfo.mipLevels = 1;
	imageInfo.arrayLayers = 1;
	imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
	imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
	imageInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
	imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	VmaAllocationCreateInfo imageAlloc = {};
	imageAlloc.usage = VMA_MEMORY_USAGE_GPU_ONLY;
	if (vmaCreateImage(device->allocator, &imageInfo, &imageAlloc, &slot.image, &slot.imageAllocation, nullptr) != VK_SUCCESS)
		return false;

	VkBufferCreateInfo bufferInfo = { VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
	bufferInfo.size = (VkDeviceSize)width * height * 4;
	bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
	VmaAllocationCreateInfo bufferAlloc = {};
	bufferAlloc.usage = VMA_MEMORY_USAGE_GPU_TO_CPU;
	bufferAlloc.flags = VMA_ALLOCATION_CREATE_MAPPED_BIT;
	VmaAllocationInfo info = {};
	if (vmaCreateBuffer(device->allocator, &bufferInfo, &bufferAlloc, &slot.buffer, &slot.bufferAllocation, &info) != VK_SUCCESS)
	{
		MGVK_ReleaseCaptureSlot(device, slot);
		return false;
	}
	slot.mapped = info.pMappedData;
	slot.width = width;
	slot.height = height;
	return true;
}

static void MGVK_RecordFrameCapture(MGG_GraphicsDevice* device, MGVK_Frame& frame, size_t slotIndex)
{
	if (!device->captureEnabled || frame.image_index == (uint32_t)-1 || device->swapchains.empty())
		return;
	if (device->captureSlots.size() < device->frames.size())
		device->captureSlots.resize(device->frames.size());
	auto& slot = device->captureSlots[slotIndex];
	auto texture = device->swapchains[frame.image_index].texture;
	uint32_t width = std::max(1u, device->swapchainWidth / (uint32_t)device->captureDownscale);
	uint32_t height = std::max(1u, device->swapchainHeight / (uint32_t)device->captureDownscale);
	if (!MGVK_EnsureCaptureSlot(device, slot, width, height))
		return;

	auto cmd = frame.commandBuffer;
	slot.pending = true;
	slot.frame = device->frame;
	// Whether a render pass drew into the presented image this frame (an image presented without being drawn is black
	// or stale: a defect in itself, reported as such).
	slot.drawn = texture->writeFrame == device->frame && texture->layouts[0] == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

	VkBufferMemoryBarrier hostBarrier = { VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
	hostBarrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
	hostBarrier.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
	hostBarrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	hostBarrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	hostBarrier.buffer = slot.buffer;
	hostBarrier.offset = 0;
	hostBarrier.size = VK_WHOLE_SIZE;

	if (!slot.drawn)
	{
		vkCmdFillBuffer(cmd, slot.buffer, 0, VK_WHOLE_SIZE, 0);
		vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 0, nullptr, 1, &hostBarrier, 0, nullptr);
		return;
	}

	auto srcImage = texture->image;
	MGVK_CmdTransitionImageLayout(cmd, srcImage, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);
	MGVK_CmdTransitionImageLayout(cmd, slot.image, VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);

	VkImageBlit blit = {};
	blit.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	blit.srcSubresource.layerCount = 1;
	blit.srcOffsets[0] = { 0, 0, 0 };
	blit.srcOffsets[1] = { (int32_t)(width * device->captureDownscale), (int32_t)(height * device->captureDownscale), 1 };
	blit.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	blit.dstSubresource.layerCount = 1;
	blit.dstOffsets[0] = { 0, 0, 0 };
	blit.dstOffsets[1] = { (int32_t)width, (int32_t)height, 1 };
	vkCmdBlitImage(cmd, srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, slot.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit,
		device->captureDownscale > 1 ? VK_FILTER_LINEAR : VK_FILTER_NEAREST);

	MGVK_CmdTransitionImageLayout(cmd, slot.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_ASPECT_COLOR_BIT);
	MGVK_CmdTransitionImageLayout(cmd, srcImage, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR, VK_IMAGE_ASPECT_COLOR_BIT);
	MGVK_CmdCopyImageToBuffer(cmd, slot.image, slot.buffer, 0, 0, 0, 0, 0, width, height, 1, VK_IMAGE_ASPECT_COLOR_BIT);
	vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 0, nullptr, 1, &hostBarrier, 0, nullptr);
}

static void MGVK_CollectFrameCapture(MGG_GraphicsDevice* device, size_t slotIndex)
{
	if (slotIndex >= device->captureSlots.size())
		return;
	auto& slot = device->captureSlots[slotIndex];
	if (!slot.pending)
		return;
	slot.pending = false;
	if (!device->captureEnabled)
		return;
	if (device->captureReady.size() >= device->captureMaxQueued)
	{
		device->captureDropped++;
		return;
	}
	vmaInvalidateAllocation(device->allocator, slot.bufferAllocation, 0, VK_WHOLE_SIZE);
	MGVK_CapturedFrame captured;
	captured.frame = slot.frame;
	captured.width = slot.width;
	captured.height = slot.height;
	captured.drawn = slot.drawn;
	size_t bytes = (size_t)slot.width * slot.height * 4;
	captured.rgba.resize(bytes);
	memcpy(captured.rgba.data(), slot.mapped, bytes);
	device->captureReady.push_back(std::move(captured));
}

MG_EXPORT mgbool MGG_FrameCapture_Start(MGG_GraphicsDevice* device, mgint downscale, mgint maxQueued)
{
	assert(device != nullptr);
	if (device->swapchain == VK_NULL_HANDLE || downscale < 1 || maxQueued < 1)
		return false;
	// The presented image is read with a blit: the swapchain format must support it.
	VkFormatProperties props = {};
	vkGetPhysicalDeviceFormatProperties(device->physicalDevice, device->colorFormat, &props);
	if ((props.optimalTilingFeatures & VK_FORMAT_FEATURE_BLIT_SRC_BIT) == 0)
		return false;
	device->captureDownscale = downscale;
	device->captureMaxQueued = (size_t)maxQueued;
	device->captureDropped = 0;
	device->captureReady.clear();
	device->captureEnabled = true;
	return true;
}

MG_EXPORT void MGG_FrameCapture_Stop(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);
	device->captureEnabled = false;
	vkDeviceWaitIdle(device->device);
	for (auto& slot : device->captureSlots)
		MGVK_ReleaseCaptureSlot(device, slot);
	device->captureSlots.clear();
	device->captureReady.clear();
}

// Copies the oldest captured frame (RGBA8, width × height × 4 bytes) into data and removes it. False when no frame is
// ready, or when data is too small (then nothing is removed; width and height give the size needed).
MG_EXPORT mgbool MGG_FrameCapture_Pop(MGG_GraphicsDevice* device, mgbyte* data, mgint dataBytes, mgint* width, mgint* height, mgulong* frame, mgbool* drawn)
{
	assert(device != nullptr);
	if (device->captureReady.empty())
		return false;
	auto& front = device->captureReady.front();
	*width = (mgint)front.width;
	*height = (mgint)front.height;
	*frame = (mgulong)front.frame;
	*drawn = front.drawn;
	if (data == nullptr || (size_t)dataBytes < front.rgba.size())
		return false;
	memcpy(data, front.rgba.data(), front.rgba.size());
	device->captureReady.pop_front();
	return true;
}

MG_EXPORT mgulong MGG_FrameCapture_Dropped(MGG_GraphicsDevice* device)
{
	assert(device != nullptr);
	return (mgulong)device->captureDropped;
}


// Etervale fork: compute dispatches, storage images and storage buffers in the frame's own command buffer
// (monogame/issues/005-compute-and-uav.md, 006-structured-buffers.md).
//
// A dispatch ends the render pass (vkCmdDispatch is not allowed inside one; the next draw begins a new pass, which keeps
// the targets' content) and is wrapped in two barriers: before it, everything the frame wrote so far (attachments,
// copies, shader writes) is made visible to the compute shader and its storage images move to the GENERAL layout;
// after it, what the shader wrote is made visible to every later stage (vertex input, shaders, indirect arguments,
// copies) and the images return to the layout their sampled descriptors expect. Global memory barriers cover the
// buffers, so no resource has to be tracked by hand between passes.

MG_EXPORT void MGG_GraphicsDevice_SetShaderBuffer(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Buffer* buffer, mgbool writable)
{
	assert(device != nullptr);
	assert(buffer == nullptr || buffer->type == MGBufferType::Storage);
	if (writable)
	{
		assert(slot >= 0 && slot < MGVK_MAX_UAV_SLOTS);
		device->uavs[(mgint)stage][slot] = { buffer, nullptr, 0 };
		device->storageDirty[(mgint)stage] |= 1u << (16 + slot);
	}
	else
	{
		assert(slot >= 0 && slot < MAX_TEXTURE_SLOTS);
		device->readBuffers[(mgint)stage][slot] = buffer;
		device->storageDirty[(mgint)stage] |= 1u << slot;
	}
}

MG_EXPORT void MGG_GraphicsDevice_SetShaderImage(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Texture* texture, mgint mip)
{
	assert(device != nullptr);
	assert(slot >= 0 && slot < MGVK_MAX_UAV_SLOTS);
	assert(texture == nullptr || (texture->isStorage && mip >= 0 && mip < (mgint)texture->info.mipLevels));
	device->uavs[(mgint)stage][slot] = { nullptr, texture, mip };
	device->storageDirty[(mgint)stage] |= 1u << (16 + slot);
}

// Everything a dispatch reads may have been written earlier in the frame by a render pass, a copy or another dispatch.
static void MGVK_BarrierBeforeDispatch(MGG_GraphicsDevice* device, VkCommandBuffer cmd, MGG_Shader* shader)
{
	VkMemoryBarrier memory = { VK_STRUCTURE_TYPE_MEMORY_BARRIER };
	memory.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT |
		VK_ACCESS_TRANSFER_WRITE_BIT | VK_ACCESS_SHADER_WRITE_BIT;
	memory.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_UNIFORM_READ_BIT |
		VK_ACCESS_INDIRECT_COMMAND_READ_BIT;

	VkImageMemoryBarrier images[MGVK_MAX_UAV_SLOTS];
	uint32_t imageCount = 0;
	const int stage = (int)MGShaderStage::Compute;
	for (int i = 0; i < MGVK_MAX_UAV_SLOTS; i++)
	{
		if ((shader->uavImageSlots & (1u << i)) == 0)
			continue;
		auto& uav = device->uavs[stage][i];
		auto& layout = uav.texture->layouts[uav.mip];
		if (layout == VK_IMAGE_LAYOUT_GENERAL)
			continue;	// the same image and mip in two slots
		auto& b = images[imageCount++];
		b = { VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER };
		b.srcAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_WRITE_BIT;
		b.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
		b.oldLayout = layout;
		b.newLayout = VK_IMAGE_LAYOUT_GENERAL;
		b.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
		b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
		b.image = uav.texture->image;
		b.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, (uint32_t)uav.mip, 1, 0, uav.texture->info.arrayLayers };
		layout = VK_IMAGE_LAYOUT_GENERAL;
	}

	vkCmdPipelineBarrier(cmd,
		VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
		VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT |
		VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
		VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
		0, 1, &memory, 0, nullptr, imageCount, images);
}

// What the dispatch wrote, visible to whatever comes next; storage images back in the layout of sampled descriptors.
static void MGVK_BarrierAfterDispatch(MGG_GraphicsDevice* device, VkCommandBuffer cmd, MGG_Shader* shader)
{
	VkMemoryBarrier memory = { VK_STRUCTURE_TYPE_MEMORY_BARRIER };
	memory.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
	memory.dstAccessMask = MGVK_StorageReadAccess | VK_ACCESS_INDEX_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
		VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

	VkImageMemoryBarrier images[MGVK_MAX_UAV_SLOTS];
	uint32_t imageCount = 0;
	const int stage = (int)MGShaderStage::Compute;
	for (int i = 0; i < MGVK_MAX_UAV_SLOTS; i++)
	{
		if ((shader->uavImageSlots & (1u << i)) == 0)
			continue;
		auto& uav = device->uavs[stage][i];
		auto& layout = uav.texture->layouts[uav.mip];
		uav.texture->writeFrame = device->frame;	// read-backs flush the frame first (MGG_Texture_GetData)
		uav.texture->frame = device->frame;
		if (layout != VK_IMAGE_LAYOUT_GENERAL)
			continue;	// the same image and mip in two slots
		auto& b = images[imageCount++];
		b = { VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER };
		b.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
		b.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_READ_BIT |
			VK_ACCESS_TRANSFER_WRITE_BIT | VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
		b.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
		b.newLayout = uav.texture->optimal_layout;
		b.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
		b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
		b.image = uav.texture->image;
		b.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, (uint32_t)uav.mip, 1, 0, uav.texture->info.arrayLayers };
		layout = uav.texture->optimal_layout;
	}

	vkCmdPipelineBarrier(cmd,
		VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
		MGVK_StorageStages | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT,
		0, 1, &memory, 0, nullptr, imageCount, images);
}

// The common part of the two dispatches: checks, render pass end, barrier, pipeline and resources. False when the
// dispatch cannot run (no compute shader set, or a storage image slot of the shader left empty); it is then skipped
// with a message instead of recording invalid commands.
static bool MGVK_BeginDispatch(MGG_GraphicsDevice* device, MGVK_Frame*& frameOut, MGG_Shader*& shaderOut)
{
	auto shader = device->shaders[(mgint)MGShaderStage::Compute];
	if (shader == nullptr || shader->computePipeline == VK_NULL_HANDLE)
	{
		fprintf(stderr, "Dispatch without a compute shader: skipped\n");
		return false;
	}
	for (int i = 0; i < MGVK_MAX_UAV_SLOTS; i++)
	{
		if ((shader->uavImageSlots & (1u << i)) != 0 && device->uavs[(int)MGShaderStage::Compute][i].texture == nullptr)
		{
			fprintf(stderr, "Dispatch of compute shader %u with storage image slot u%d empty: skipped\n", shader->id, i);
			return false;
		}
	}

	auto& frame = device->frames[device->frameIndex];
	assert(frame.is_recording);

	// The constants are appended to the frame's uniform buffer as for a draw (and it may be submitted early when full).
	MGVK_EnsureUniformSpace(device, frame);

	auto cmd = frame.commandBuffer;
	MGVK_EndRenderPass(device, cmd);
	device->renderTargetDirty = true;

	MGVK_BarrierBeforeDispatch(device, cmd, shader);

	vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, shader->computePipeline);
	device->lastComputePipeline = shader->computePipeline;   // Etervale fork, issue 012
	if (!shader->bindings.empty())
	{
		VkDescriptorSet set = VK_NULL_HANDLE;
		uint32_t dynamicOffset = 0;
		MGVK_BindShaderResources(device, device->frame, shader, &set, shader->uniformSlots ? &dynamicOffset : nullptr);
		vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, shader->computeLayout, 0, 1, &set,
			shader->uniformSlots ? 1 : 0, &dynamicOffset);
	}
	device->storageDirty[(int)MGShaderStage::Compute] = 0;

	frameOut = &frame;
	shaderOut = shader;
	return true;
}

MG_EXPORT void MGG_GraphicsDevice_Dispatch(MGG_GraphicsDevice* device, mgint groupsX, mgint groupsY, mgint groupsZ)
{
	assert(device != nullptr);
	if (groupsX <= 0 || groupsY <= 0 || groupsZ <= 0)
		return;

	MGVK_Frame* frame;
	MGG_Shader* shader;
	if (!MGVK_BeginDispatch(device, frame, shader))
		return;
	vkCmdDispatch(frame->commandBuffer, (uint32_t)groupsX, (uint32_t)groupsY, (uint32_t)groupsZ);
	MGVK_BarrierAfterDispatch(device, frame->commandBuffer, shader);
}

// The group counts are three uints at byteOffset in a storage buffer (VkDispatchIndirectCommand), typically written by
// an earlier dispatch: the barrier after that dispatch makes them visible to the indirect-argument read.
MG_EXPORT void MGG_GraphicsDevice_DispatchIndirect(MGG_GraphicsDevice* device, MGG_Buffer* arguments, mgint byteOffset)
{
	assert(device != nullptr);
	assert(arguments != nullptr && arguments->type == MGBufferType::Storage);
	assert(byteOffset >= 0 && (byteOffset % 4) == 0 && byteOffset + 12 <= arguments->dataSize);

	MGVK_Frame* frame;
	MGG_Shader* shader;
	if (!MGVK_BeginDispatch(device, frame, shader))
		return;
	arguments->frame = device->frame;
	arguments->used = true;
	vkCmdDispatchIndirect(frame->commandBuffer, arguments->buffer, (VkDeviceSize)byteOffset);
	MGVK_BarrierAfterDispatch(device, frame->commandBuffer, shader);
}

// The barriers around a copy into a storage buffer: earlier reads and writes of the range finish first (frames in
// flight included: a pipeline barrier's first scope reaches back through earlier submissions on the queue), and later
// reads see the new bytes.
static void MGVK_RecordStorageUpload(VkCommandBuffer cmd, MGG_Buffer* buffer, VkBuffer staging, mgint offset, mgint bytes)
{
	VkBufferMemoryBarrier barrier = { VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
	barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.buffer = buffer->buffer;
	barrier.offset = (VkDeviceSize)offset;
	barrier.size = (VkDeviceSize)bytes;
	barrier.srcAccessMask = MGVK_StorageReadAccess;
	barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
	vkCmdPipelineBarrier(cmd, MGVK_StorageStages, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 1, &barrier, 0, nullptr);

	VkBufferCopy region = { 0, (VkDeviceSize)offset, (VkDeviceSize)bytes };
	vkCmdCopyBuffer(cmd, staging, buffer->buffer, 1, &region);

	barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
	barrier.dstAccessMask = MGVK_StorageReadAccess;
	vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, MGVK_StorageStages, 0, 0, nullptr, 1, &barrier, 0, nullptr);
}

// Fills bytes [offset, offset + dataBytes) of a storage buffer. The bytes go to a staging buffer at once (the caller
// may reuse its memory on return); the copy into the buffer is:
// - on the render thread while a frame records: recorded in the frame's command buffer, ordered with the draws and
//   dispatches as in OpenGL (earlier commands read the old bytes, later ones the new), the staging buffer freed once the
//   frame is complete (the same staging scheme as MGG_Texture_SetData);
// - on the render thread between frames: submitted at once on its own command buffer, without waiting; frames submitted
//   after the call see the new bytes, and the staging buffer is freed once the copy is done (MGVK_CleanupPendingTransfers);
// - on any other thread: the asynchronous upload of large data (a BVH, monogame/issues/006-structured-buffers.md). The
//   caller's thread does everything: the staging allocation, the copy into it, the submission of the copy on a command
//   pool of its own, the wait for it and the release, so the call returns with the bytes in the buffer and every frame
//   submitted afterwards sees them, while the render thread never waits. The staging memory is a dedicated allocation:
//   a pooled one of 64 MB made VMA create a new memory block (vkAllocateMemory, 35 ms on the GTX 1050 Ti) under the lock
//   of the memory type's block list, which the render thread takes for its own staging buffers, and the frame being
//   presented waited for it (--check-compute: one 45 ms frame; none with the dedicated allocation).
static void MGVK_UploadStorageOffThread(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataBytes)
{
	VkBufferCreateInfo bufferInfo = { VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO };
	bufferInfo.size = (VkDeviceSize)dataBytes;
	bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
	bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
	VmaAllocationCreateInfo allocInfo = {};
	allocInfo.usage = VMA_MEMORY_USAGE_CPU_ONLY;
	allocInfo.flags = VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT;
	MGG_Buffer staging;
	VkResult res = vmaCreateBuffer(device->allocator, &bufferInfo, &allocInfo, &staging.buffer, &staging.allocation, nullptr);
	VK_CHECK_RESULT(res);
	VK_SET_OBJECT_NAME(device->device, staging.buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Buffer_SetStorageData::dedicatedStaging");
	void* dest;
	vmaMapMemory(device->allocator, staging.allocation, &dest);
	memcpy(dest, data, dataBytes);
	vmaUnmapMemory(device->allocator, staging.allocation);

	VkCommandPoolCreateInfo poolInfo = { VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO };
	poolInfo.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT;
	poolInfo.queueFamilyIndex = device->graphicsQueueFamily;
	VkCommandPool pool;
	res = vkCreateCommandPool(device->device, &poolInfo, nullptr, &pool);
	VK_CHECK_RESULT(res);
	VkCommandBufferAllocateInfo cmdInfo = { VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO };
	cmdInfo.commandPool = pool;
	cmdInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	cmdInfo.commandBufferCount = 1;
	VkCommandBuffer cmd;
	res = vkAllocateCommandBuffers(device->device, &cmdInfo, &cmd);
	VK_CHECK_RESULT(res);
	VkCommandBufferBeginInfo beginInfo = { VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO };
	beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
	vkBeginCommandBuffer(cmd, &beginInfo);
	MGVK_RecordStorageUpload(cmd, buffer, staging.buffer, offset, dataBytes);
	vkEndCommandBuffer(cmd);

	VkFenceCreateInfo fenceInfo = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
	VkFence done;
	vkCreateFence(device->device, &fenceInfo, nullptr, &done);
	VK_SET_OBJECT_NAME(device->device, done, VK_OBJECT_TYPE_FENCE, "MGG_Buffer_SetStorageData.offThreadDone");
	{
		std::lock_guard lock(device->queueMutex);
		VkSubmitInfo submit = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
		submit.commandBufferCount = 1;
		submit.pCommandBuffers = &cmd;
		res = vkQueueSubmit(device->queue, 1, &submit, done);
		VK_CHECK_RESULT(res);
	}
	vkWaitForFences(device->device, 1, &done, VK_TRUE, UINT64_MAX);

	vkDestroyFence(device->device, done, nullptr);
	vkDestroyCommandPool(device->device, pool, nullptr);
	vmaDestroyBuffer(device->allocator, staging.buffer, staging.allocation);
}

MG_EXPORT void MGG_Buffer_SetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataBytes)
{
	assert(device != nullptr);
	assert(buffer != nullptr && buffer->type == MGBufferType::Storage);
	assert(data != nullptr && dataBytes > 0);
	assert(offset >= 0 && offset + dataBytes <= buffer->dataSize);

	if (std::this_thread::get_id() != device->renderThread)
	{
		MGVK_UploadStorageOffThread(device, buffer, offset, data, dataBytes);
		return;
	}

	MGG_Buffer staging;
	MGVK_BufferCreate(device, dataBytes, VK_BUFFER_USAGE_TRANSFER_SRC_BIT, VMA_MEMORY_USAGE_CPU_ONLY, &staging);
	VK_SET_OBJECT_NAME(device->device, staging.buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Buffer_SetStorageData::stagingBuffer");
	void* dest;
	vmaMapMemory(device->allocator, staging.allocation, &dest);
	memcpy(dest, data, dataBytes);
	vmaUnmapMemory(device->allocator, staging.allocation);

	if (device->frameIndex < device->frames.size() && device->frames[device->frameIndex].is_recording)
	{
		auto& frame = device->frames[device->frameIndex];
		MGVK_EndRenderPass(device, frame.commandBuffer);
		device->renderTargetDirty = true;
		MGVK_RecordStorageUpload(frame.commandBuffer, buffer, staging.buffer, offset, dataBytes);
		device->frameStaging.push_back({ device->frame, staging });
		buffer->frame = device->frame;
		buffer->used = true;
		return;
	}

	std::lock_guard lock(device->transferMutex);
	MGVK_Transfer transfer;
	transfer.owner = nullptr;
	transfer.cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
	transfer.staging = staging;
	MGVK_RecordStorageUpload(transfer.cmd, buffer, staging.buffer, offset, dataBytes);
	vkEndCommandBuffer(transfer.cmd);

	VkFenceCreateInfo fenceInfo = { VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
	vkCreateFence(device->device, &fenceInfo, nullptr, &transfer.done);
	VK_SET_OBJECT_NAME(device->device, transfer.done, VK_OBJECT_TYPE_FENCE, "MGG_Buffer_SetStorageData.done");
	{
		std::lock_guard lock2(device->queueMutex);
		VkSubmitInfo submit = { VK_STRUCTURE_TYPE_SUBMIT_INFO };
		submit.commandBufferCount = 1;
		submit.pCommandBuffers = &transfer.cmd;
		vkQueueSubmit(device->queue, 1, &submit, transfer.done);
	}
	device->pendingTransfers.push_back(transfer);
}

// Reads bytes [offset, offset + dataBytes) of a storage buffer back to the CPU, waiting for the GPU: a frame that used
// the buffer and is still recording is submitted first (as MGG_Texture_GetData does for a render target). For tests and
// tools, not for every frame.
MG_EXPORT void MGG_Buffer_GetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataBytes)
{
	assert(device != nullptr);
	assert(buffer != nullptr && buffer->type == MGBufferType::Storage);
	assert(data != nullptr && dataBytes > 0);
	assert(offset >= 0 && offset + dataBytes <= buffer->dataSize);

	std::lock_guard lock(device->transferMutex);
	std::lock_guard lock2(device->queueMutex);

	auto& frame = device->frames[device->frameIndex];
	bool restart = false;
	if (frame.is_recording && buffer->used && buffer->frame == device->frame)
	{
		MGVK_FlushCommands(device, frame);
		restart = true;
	}

	MGG_Buffer readback;
	MGVK_BufferCreate(device, dataBytes, VK_BUFFER_USAGE_TRANSFER_DST_BIT, VMA_MEMORY_USAGE_GPU_TO_CPU, &readback);
	VK_SET_OBJECT_NAME(device->device, readback.buffer, VK_OBJECT_TYPE_BUFFER, "MGG_Buffer_GetStorageData::readback");

	VkCommandBuffer cmd = MGVK_BeginNewCommandBuffer(device, device->transferPool);
	VkBufferMemoryBarrier barrier = { VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
	barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	barrier.buffer = buffer->buffer;
	barrier.offset = (VkDeviceSize)offset;
	barrier.size = (VkDeviceSize)dataBytes;
	barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_WRITE_BIT;
	barrier.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
	vkCmdPipelineBarrier(cmd, MGVK_StorageStages, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 1, &barrier, 0, nullptr);
	VkBufferCopy region = { (VkDeviceSize)offset, 0, (VkDeviceSize)dataBytes };
	vkCmdCopyBuffer(cmd, buffer->buffer, readback.buffer, 1, &region);
	VkBufferMemoryBarrier host = { VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER };
	host.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	host.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
	host.buffer = readback.buffer;
	host.size = VK_WHOLE_SIZE;
	host.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
	host.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
	vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0, 0, nullptr, 1, &host, 0, nullptr);
	MGVK_ExecuteAndFreeCommandBuffer(device, cmd, device->transferPool);

	void* src;
	vmaMapMemory(device->allocator, readback.allocation, &src);
	vmaInvalidateAllocation(device->allocator, readback.allocation, 0, VK_WHOLE_SIZE);
	memcpy(data, src, dataBytes);
	vmaUnmapMemory(device->allocator, readback.allocation);
	vmaDestroyBuffer(device->allocator, readback.buffer, readback.allocation);

	if (restart)
		MGVK_BeginCommandBuffer(frame.commandBuffer);
}

# DSH「标准（standard）模式」权限说明书

> 用途：把本会话（Web GUI，`standard` 智能体预设，`workspace-write` 权限预设）实际生效的权限模型抄给别的工程做参考。
> 全部结论都来自本机 DSH 安装包与当前会话的投影状态，证据路径见文末第 8 节。
> 版本：`@deepseek-ai/*` 0.2.0-rc.2（`D:\deepseek\resources\app.asar`）。

---

## 1. 一句话结论

「标准模式」不是一个权限档，而是**智能体预设（agent preset）** `standard`；它决定挂载哪些插件/工具，**本身不含任何权限配置**。
真正决定权限的是与它正交的**权限预设（permission preset）**，本会话实测为：

| 项 | 实测值 | 说明 |
|---|---|---|
| `agentPreset` | `standard` | 标准模式卡片 |
| `permissions.preset` | `workspace-write` | 权限选择器当前档位 |
| `permissions.sandbox` | `workspace-write` | 文件沙箱档位 |
| `permissions.approval` | `ask` | 审批策略 |
| `permissions.seeded` | `false` | 会话创建时钉下的默认值 |

即：**只读式观察不受限，写/改/删只能落在会话工作区（加一个私有临时目录）内，敏感动作一次一问、无审批者即拒绝。**

---

## 2. 别混淆：DSH 里有两套互相正交的「模式」

| | 智能体预设（模式卡片） | 权限预设（权限选择器） |
|---|---|---|
| 作用 | 组合哪些插件与工具（能力清单） | 只捆绑两个旋钮：文件沙箱档 + 审批策略 |
| 取值 | `standard`(标准)、`cordis`、`minimal`、`ptc`、Creator 自定义 | `read-only`(仅可查看)、`workspace-write`(工作区内修改)、`danger-full-access`(完全权限)、`auto`(Auto review, EXP) |
| 定义位置 | `dsh-web-app/presets/*.patch.yml` | `dsh-base/cordis.patch.yml` 的 `permission` 行 |
| 当前会话 | `projcache.agentPreset = "standard"` | `projcache.permissions = {workspace-write, ask}` |

关键点：

- `standard.patch.yml` 全文**没有任何** `sandbox` / `approval` / `permission` 配置行 —— 选 mode 不等于给权限。
- 权限档有**会话级**与**默认级**两层：设置里的 `defaultPreset` 只影响**将来新建**的会话；当前会话靠 `/permission <preset>`（或输入框选择器）切换，切换会写进会话日志（`permission/preset`、`sandbox/mode`、`approval/policy` 事件），重启靠重放恢复，且两个会话互不影响。
- 开关还可以被部署环境变量 `DSH_PERMISSION_MODE` 覆写；审批策略由它推导：`danger-full-access → never`，其余 `→ ask`。

---

## 3. 三档权限的确切含义

| 权限档 | 文件效果（sandbox mode） | 审批 | 备注 |
|---|---|---|---|
| 仅可查看 `read-only` | 拒绝一切写/改/删（`/dev/null` 一类必需 sink 除外） | `ask` | 部署端的**失败兜底默认值**（未配置时用这档） |
| 工作区内修改 `workspace-write` | 只允许写在工作区根 + 后端私有临时目录 | `ask` | **标准模式默认**，也是 DSH 出厂默认 |
| 完全权限 `danger-full-access` | 不做任何文件限制 | `never` | 审批直接确定性拒绝，模型也被明确告知不要再请求豁免 |
| Auto review `auto` (EXP) | = 完全权限沙箱 | `ask` | 固定实现，仅当前会话可选，不能设为默认 |

沙箱模式的**词表只表达文件效果**：不留网络、进程、syscall、设备、凭据限制 —— 这是设计约束，不是遗漏。要更强的隔离只能换容器/微VM/远端执行器。

---

## 4. `workspace-write` 在 Windows 上的真实边界（最值得抄的一层）

### 4.1 可写 / 不可写

| 目标 | 结果 |
|---|---|
| 会话不可变 `cwd`（本会话 = `D:\SteamLibrary\Mods\rhodes-doctor-mod`） | 可写 |
| 该 (session, workspace) 对的**私有**临时目录（`TMP`/`TEMP` 在 spawn 前被改写指向它） | 可写 |
| 工作站其它任何路径（包括 `C:\Users\...\AppData\Local\Temp` 的公共区、`C:\Public`、`C:\` 根） | 写/删/重命名拒绝 |
| 读取、列目录、元数据、只读 watch | **不受限**（任何调用者可读的文件都能读，含别的工作区） |
| 网络（开 socket）、进程可见性 | **不受限** |

拒绝以事实形式返回：结果带 `sandbox: { mode, denied: true }`，工具层渲染成 `[sandbox: file access denied under <mode> mode]`，并追加同一轮内可用的升级提示。

### 4.2 实现机制（Windows）

- 复制调用者令牌为 `WRITE_RESTRICTED` 受限令牌，并把完整性级别降到 **Low**；Windows 做两次访问检查，且强制完整性标签只放行被标 Low 的对象。
- 每个 workspace 一个**确定性 SID**（由规范化路径派生，ACE 一次物化后常驻复用）；每个 (session, workspace) 对再给一个**随机私有 temp 目录 + 独立可撤销 SID**，所以同 workspace 的会话共享工作区写权、但互不共享 temp 写权。
- 每次授权同时写入三样：能力 SID 的 allow ACE、对 world 的 `FILE_DELETE_CHILD` deny（否则可通过父目录权限删掉别的授权根里的文件）、Low 标签 —— 一次 `SetNamedSecurityInfoW` 完成。
- **fail closed**：任一 Win32 调用失败则不 spawn（绝不无约束启动）；无可用 runner 时报 `SANDBOX_UNAVAILABLE`，绝不静默放行。

### 4.3 已知缺口与副作用（抄过去时要写进文档）

- **读侧完全不设防**；`read-only` 因此是"写侧只读"，不是保密边界。
- NTFS **硬链接**是同一文件对象的别名，外部别名可绕过路径白名单。
- 被别的 AppContainer 工具打过包 SID（`S-1-15-2-…`）的文件，Low 完整性子进程**读不到**。
- 受限子进程里 `spawn(..., { stdio: 'pipe' })` 会因命名管道 EPERM 失败（继承/忽略 stdio 可用；PowerShell 匿名管道不受影响）。
- `NUL` 两档都可写；`Set-Content NUL` 两档都失败，`> $null` 正常。
- 两档都缺 `Authenticated Users`，因此 CIM/WMI 类 cmdlet、`Get-ComputerInfo` 不可用。
- 不能用 `CREATE_NO_WINDOW`/`CREATE_NEW_CONSOLE`（子进程 0xC0000142 退出），共享宿主控制台。
- **PowerShell 语言模式随档位变化**：`read-only` 下 pwsh 保守进入 `ConstrainedLanguage`（`Add-Type`、非核心 .NET 静态调用、COM、反射失败）；`workspace-write` 能完成 AppLocker 探测，保持 `FullLanguage`。
- 工作区的**常驻 Low 标签与 ACE 不会回收**（跨会话、跨重启、同卷移动仍有效），副作用是同用户下其它 Low 完整性进程也能写该树；是写边界的代价，不是 bug。
- 授权是"规范化后再判包含"的用户态策略（fs 工具那条路径），属于策略栅栏而非内核边界；重命名/符号链接祖先的 TOCTOU 已用写入前重新规范化收窄，但未消除。

---

## 5. 审批（`ask`）的确切语义

- **一次性**：一个敏感动作一次决策；结果词表有 `allowed-once` / `rejected` / `cancelled` / `unavailable`，**没有** allow-always、规则记忆、授权存储或撤销。
- **fail closed**：没有可用 answerer（或 answerer 抛错、返回非法词）→ `unavailable` → 动作被拒绝。服务自己从不弹窗，交互式部署必须组合至少一个 answerer。
- 请求只携带：agent、tool 名、可选 call id、reason（+ 可选的本地化展示文案）；**不含工具参数**。
- 只能在一轮对话内请求：空闲/轮次之间的调用先抛错、连审计都不写。
- **升级重试**：被拒后模型可用 `sandbox_permissions`（够用的最窄更宽档）+ 一句 `justification` 原样重试一次。阶梯是闭合表：`read-only → workspace-write|danger-full-access`，`workspace-write → danger-full-access`；重试**只对那一次调用**生效，必须人工批准；重复当前档不需要批准；更窄的档非法。
- `never` 档：在派发之前确定性拒绝，并给模型一条"审批已禁用、不要请求沙箱升级"的 sourced user message。
- 模型看不到审批 UI 和审计事件（`approval/asked`/`approval/decided` 只进日志），只看到工具结果 + 当前策略文本（策略或档位变化时**追加**一份完整运行时上下文快照，保持 KV 前缀不变）。

---

## 6. 标准模式额外挂载的"策略类"护栏（与权限相关，容易漏抄）

| 插件 | 行为 |
|---|---|
| `dsh-fs-observation-policy` | `write` 覆盖已存在文件前必须先 `read`；`edit` 必须先 `read`；读过后文件被改 → `FS_STALE_VERSION`（提示重读再试）；未读 → `FS_NOT_OBSERVED`，工具层渲染为 `cannot modify "<path>": file has not been read — read the file, then retry`。读一个不存在的路径=确认缺失，可授权后续受保护创建。会话恢复后观察记录不持久，需重读。 |
| `dsh-plan-mode`（isolate） | 计划模式下不改文件、不改配置、不提交；只有 `exit_plan_mode` 通过后才开始实施。 |
| `dsh-compaction-basic` + `dsh-compaction-tool-result-pruner` | 超过 8192 字符的工具结果裁剪为 head 4096 + tail 1024（阈值随预设可调）。 |
| `dsh-agent-instructions` | 指令文件读取上限 `maxBytes: 65536`。 |
| `dsh-tool-fs-search` | `sampleOverCapGlobResults: false`（超上限不采样）。 |
| `dsh-tool-web` | 搜索 + 抓取开启，`searchTimeoutMs: 60000`；网络策略不在权限档词表内。 |
| 被显式禁用 | `tool-bash`（Windows 上）、`tool-ralph`、`subagent_codex`、`subagent_claude_code`、`plugin-manager/tools`。 |

---

## 7. 照搬到别的工程时的最小骨架与检查清单

### 7.1 配置骨架（DSH 原生写法）

```yaml
- id: sandbox
  name: '@deepseek-ai/dsh-sandbox-local'          # 平台 runner 提供者（Windows → ACL 受限令牌）
- id: sandbox-policy
  name: '@deepseek-ai/dsh-sandbox-policy'
  config:
    mode: workspace-write                          # 部署默认档；兜底默认其实是 read-only
    workspaceRoot: !!js process.cwd()              # 无会话 cwd / 无 agent 调用时的兜底根
- id: approval
  name: '@deepseek-ai/dsh-user-approval'
  config:
    policy: ask                                    # danger-full-access 时推导为 never
- id: permission
  name: '@deepseek-ai/dsh-permission-presets'
  config:
    presets:
      read-only:          { sandbox: read-only,          approval: ask }
      workspace-write:    { sandbox: workspace-write,    approval: ask }
      danger-full-access: { sandbox: danger-full-access, approval: never }
    # defaultPreset 只在组合默认值与预设表对不上时才必须显式写
```

### 7.2 复刻检查清单

1. 白名单必须**规范化后再判包含**（`canonicalize` → `containment`），符号链接祖先后替换要重判。
2. 白名单外的一切**写、删、重命名**拒绝；只读观察一律放行（别把"读"也关掉，否则工具全废）。
3. 临时目录必须**每个会话私有**、且与工作区**互不包含**；spawn 前改写 `TMP`/`TEMP`，不要隐式授予系统 temp 根。
4. 拒绝要返回**可机读的错误码 / 事实位**（如 `FS_SANDBOX_DENIED`、`sandbox.denied`），不要靠解析 stderr 猜。
5. **无 runner 必须 fail closed**（`SANDBOX_UNAVAILABLE`），永不静默无约束执行。
6. 被拒时给模型一条**同轮可用的最窄升级提示**；升级只批准**一次调用**，且必须走审批。
7. 审批**无 answerer 即 fail closed**；只做 one-shot，不实现 allow-always（否则等于永久扩权）。
8. 策略文本**追加**到上下文（append-only），不要改 system prompt 前缀，否则 KV 缓存全失效。
9. 每档策略要能被**会话级切换且写进日志**（可重放恢复），别只做进程级全局开关。
10. 文档里显式声明**不覆盖**的东西：网络、进程、设备、凭据、读侧保密性。

---

## 8. 证据与自查路径

**当前会话实测值**（可直接打开验证）：
- `%USERPROFILE%\.dsh\storages\session_projcache\sessions\session-c0c055a8-e28f-4ed8-9b02-cb43e57609b3.json`
  → `rows.agentPreset.val = "standard"`，`rows.permissions.val = { preset: "workspace-write", sandbox: "workspace-write", approval: "ask", seeded: false }`

**出厂配置与预设定义**（位于 `D:\deepseek\resources\app.asar` 内，asar 需解包后阅读）：
- `dsh/node_modules/@deepseek-ai/dsh-base/cordis.patch.yml`
  - L223–262：`sandbox` / `sandbox-policy`（`mode: DSH_PERMISSION_MODE ?? 'workspace-write'`，`workspaceRoot: process.cwd()`）/ `bash-sandbox` / `pwsh-sandbox` / `approval`（`policy` 推导）/ `permission`（三档预设表）
  - L516–519：`fs-sandbox` 挂载
  - L461–490：`web` / `tool-web` 行（网络能力不在权限档内）
- `dsh/node_modules/@deepseek-ai/dsh-web-app/presets/standard.patch.yml`（146 行，标准模式完整插件清单）
- `dsh/node_modules/@deepseek-ai/dsh-web-app/presets/{cordis,minimal,ptc}.patch.yml`（同目录其它模式，便于对照）
- `dsh/node_modules/@deepseek-ai/dsh-web-app/cordis.patch.yml` L311–312、L388–389（`ui-approval` / `ui-permission` 两个 GUI 出入口）

**语义来源（包 README，逐个可查）**：
- `dsh-permission-presets/README.md` —— 预设表、`custom`/`auto` 保留名、会话钉定与默认值
- `dsh-sandbox-policy/README.md` —— 档位词表、按调用解析、模型可见策略文本
- `dsh-sandbox/README.md` —— 升级阶梯、拒绝标记、fail-closed 原文
- `dsh-fs-sandbox/README.md` —— 读写栅栏、可写根推导
- `dsh-pwsh-sandbox/README.md` —— Windows 各档文件效果表、私有 temp、`SANDBOX_UNAVAILABLE`
- `dsh-sandbox-windows-acl/README.md` —— 受限令牌机制、已验证边界（第 120–134 行那段是缺口清单）
- `dsh-user-approval/README.md` —— `ask` / `never`、answerer 组合、一次性授权
- `dsh-fs-observation-policy/README.md` —— 读后写/编辑策略与错误码
- `dsh-client-ui-permission-presets/README.md` —— GUI 内置中文标签：仅可查看 / 工作区内修改 / 完全权限 / Auto review

**阅读 asar 内文件的办法**：`app.asar` 头部是 pickle 包裹的 JSON 目录（`uint32@4` = headerSize，数据区起点 `8 + headerSize`，文件项里的 `offset` 相对该起点）。写个几十行的 Node 脚本按目录项 `readSync` 解出目标文件即可（本次即用此法，未改动安装目录）。

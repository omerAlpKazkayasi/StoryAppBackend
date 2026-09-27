# StoryApp Backend — Antigravity Implementation Plan

> Bu doküman StoryApp backend'inin **sıfırdan** geliştirilme sırasını tanımlar.
>
> Başlangıç durumu: repository içinde çalışan backend kodu, migration veya korunması gereken aktif database şeması yoktur.
>
> Eski `StoryAppDbV3` yalnızca referans niteliğindedir. Yeni backend eski migration geçmişine veya eski auth/user yapısına uyumlu olmak zorunda değildir.

---

# 0. Temel Kararlar

## 0.1 Hedef

StoryApp, mobil cihazlarda kullanılan dallanan interaktif hikâye uygulamasıdır.

Ana kullanıcı akışı:

```text
APP START
   |
   v
Guest identity oluştur / mevcut token ile devam et
   |
   v
HOME
[Profile] [Settings] [Play]
                     |
                     v
                  Themes
                     |
                Theme seç
                     |
                     v
           Backend Universe seçer
                     |
           Backend Ready Story seçer
                     |
                     v
            ReadingSession başlat
                     |
                  RootNode
                     |
                  Choice
                     |
                Next Node
                     |
              Back / Continue
                     |
                   Ending
```

Mobil client mümkün olduğunca az orchestration yapmalıdır.

Universe ve Story seçimini backend yapar.

---

## 0.2 Teknoloji

- C#
- .NET 10
- ASP.NET Core Web API
- REST
- Entity Framework Core
- SQL Server
- async/await
- Dependency Injection
- JWT Bearer Authentication
- Refresh Token Rotation
- ProblemDetails tabanlı global exception handling
- Integration test öncelikli test yaklaşımı

---

## 0.3 Solution Yapısı

Başlangıçta gereksiz proje/katman oluşturma.

Tercih edilen yapı:

```text
StoryApp.Api
StoryApp.Application
StoryApp.Domain
StoryApp.Infrastructure
StoryApp.Tests
```

### StoryApp.Domain

- Entity
- Enum
- Temel domain sabitleri / küçük domain kuralları

Başka katmanlara bağımlı olmaz.

### StoryApp.Application

- Use-case/service interface'leri
- Business flow
- Request/Response modelleri
- Mapping
- Gerekli validation

DTO'lar için ayrıca `Contracts` projesi açma. Gerçek bir ihtiyaç oluşursa sonradan ayrılabilir.

### StoryApp.Infrastructure

- EF Core
- AppDbContext
- Entity Configuration
- Repository implementasyonları gerekiyorsa
- Authentication/token infrastructure
- Harici servisler
- AI provider entegrasyonu ileride

### StoryApp.Api

- Controller
- Authentication/Authorization wiring
- DI composition
- Middleware / exception handling
- Swagger/OpenAPI

### StoryApp.Tests

- API integration testleri
- Kritik business flow testleri

---

## 0.4 Mimari Kural

İlk hedef:

```text
Controller
   |
   v
Focused Application Service
   |
   v
EF Core / gerektiğinde feature-specific repository
   |
   v
SQL Server
```

Şunları başlangıçta EKLEME:

- GenericRepository<T>
- CQRS
- MediatR
- Event Bus
- Event Sourcing
- Redis
- Distributed Cache
- Microservice
- Graph Database
- Specification Pattern
- Generic BaseService
- Gereksiz UnitOfWork wrapper

EF Core `DbContext` doğrudan yeterliyse ayrıca repository oluşturma.

Gerçek bir feature sorgu/aggregate sınırı gerektirirse feature-specific repository değerlendirilebilir.

---

# 1. Story Domain — Korunacak Ana Model

Story tarafındaki ana model doğru kabul edilir ve bu yapı korunur:

```text
Theme
  |
  +-- StoryUniverse
        |
        +-- Region
        +-- Character
        +-- StoryObject
        +-- EducationFact
        |
        +-- Story
              |
              +-- RootNodeId
              |
              +-- StoryNode
                    |
                    +-- StoryNodeScene
                    +-- StoryNodeMemory
                    |
                    +-- StoryTransition
                           |
                           +-- ToNode
```

Story tek büyük text alanı değildir.

Story içeriği:

```text
StoryNode
  -> StoryNodeScene[]
```

olarak tutulur.

Bir node birden fazla scene içerebilir.

---

# 2. Bilerek Kullanılmayacak Eski Yapılar

Yeni backend'e aşağıdaki eski yapılar taşınmayacaktır:

## StoryTypePartPlan

Eski:

```text
StoryTypePartPlans
```

tablosu artık kullanılmıyor.

Yeni projede başlangıçta şunlar OLMAYACAK:

- StoryTypePartPlan entity
- StoryTypePartPlans DbSet
- StoryTypePartPlanConfiguration
- IStoryTypePlanRepository
- StoryTypePlanRepository
- StoryTypePartPlan DI registration

AI generation sırasında tekrar gerçekten ihtiyaç doğarsa yeni gereksinime göre tasarlanır.

Eski modeli geri getirme.

## StoryNodeType

Başlangıç node'u:

```text
Story.RootNodeId
```

ile belirlenir.

Ending:

```text
StoryNode.IsEnding
```

ile belirlenir.

Bu nedenle `StoryNodeType = Start / Story / Ending` başlangıç modeline eklenmez.

İki source of truth oluşturma.

## Eski Auth

Yeni projeye taşınmayacak:

- Firebase login akışı
- DemoUserId
- eski Users tasarımı
- raw RefreshToken saklama yaklaşımı
- eski auth endpointleri
- eski auth migration'ları

Auth sıfırdan tasarlanacaktır.

---

# Faz 1 — Solution Bootstrap

## Amaç

Boş repository'den compile edilen minimum solution oluştur.

## Oluştur

```text
StoryApp.sln

src/
  StoryApp.Api/
  StoryApp.Application/
  StoryApp.Domain/
  StoryApp.Infrastructure/

tests/
  StoryApp.Tests/
```

## Project References

```text
Api
 ├─ Application
 └─ Infrastructure

Application
 └─ Domain

Infrastructure
 ├─ Application
 └─ Domain

Domain
 └─ hiçbir project reference yok

Tests
 └─ gerekli projeler
```

Circular dependency oluşturma.

## Paketler

Sadece ilk aşamada gerçekten gereken paketleri ekle:

- ASP.NET Core standard dependencies
- EF Core
- EF Core SQL Server
- EF Core Design
- Swagger/OpenAPI gerekiyorsa

Auth paketlerini Auth fazında ekle.

AI provider paketlerini şimdiden ekleme.

## Program.cs

İlk aşamada sadece:

- Controllers
- OpenAPI/Swagger
- Infrastructure registration
- Application registration gerekiyorsa
- HTTPS / basic pipeline

çalışır durumda olsun.

Henüz feature endpoint yazma.

## Çıktı

- solution build başarılı
- project reference'lar doğru
- gereksiz package yok

Migration oluşturma.

---

# Faz 2 — Domain Foundation

## Amaç

Story ve reading domain entity'lerini temiz şekilde oluştur.

## 2.1 BaseEntity

Başlangıç modeli:

```text
Id : Guid
CreatedAt : DateTime
UpdatedAt : DateTime?
```

Guid proje standardıdır.

Tüm entity'leri zorla BaseEntity'den türetme.

Audit alanına gerçekten ihtiyacı olan persistent domain entity'lerinde kullan.

---

## 2.2 Theme

Alanlar:

```text
Id
Name
Description?
Icon?
Color?
SortOrder
IsActive
CreatedAt
UpdatedAt
```

İlişki:

```text
Theme 1 --- N StoryUniverse
```

---

## 2.3 StoryUniverse

Alanlar:

```text
Id
ThemeId
Name
Description
Tone?
MinimumAge
MaximumAge
IsActive
CreatedAt
UpdatedAt
```

İlişkiler:

```text
Theme
Regions
Characters
Objects
EducationFacts
Stories
```

`MagicLevel` başlangıçta eklenmez.

---

## 2.4 Region

Alanlar:

```text
Id
StoryUniverseId
Code
Name
Description
Atmosphere?
VisualStyle?
NaturalElements?
MinimumAge
MaximumAge
IsActive
CreatedAt
UpdatedAt
```

---

## 2.5 Character

Alanlar:

```text
Id
StoryUniverseId
RegionId
Code
Name
Description
Personality
Motivation?
SpeakingStyle?
VisualDescription?
SpecialAbility?
MinimumAge
MaximumAge
CanBeMainCharacter
CanBeSupportingCharacter
IsActive
CreatedAt
UpdatedAt
```

---

## 2.6 StoryObject

Alanlar:

```text
Id
StoryUniverseId
Code
Name
Description
VisualDescription?
StoryFunction?
IsMagical
IsActive
CreatedAt
UpdatedAt
```

---

## 2.7 EducationFact

Alanlar:

```text
Id
StoryUniverseId
RegionId?
Topic
Fact
UsageHint?
MinimumAge
MaximumAge
IsVerified
IsActive
CreatedAt
UpdatedAt
```

Navigation:

```text
Region?
```

olmalıdır.

`RegionId` nullable ise navigation da nullable olmalıdır.

---

# Faz 3 — Story Graph Model

## 3.1 Story

Alanlar:

```text
Id
UniverseId
StoryType
ChildAge
Language
Title?
Status
RootNodeId?
FailureReason?
CompletedAt?
CreatedAt
UpdatedAt
```

`StoryStatus`:

```text
Generating
Ready
Failed
```

Reading tarafında sadece:

```text
Status == Ready
```

olan Story'ler kullanılabilir.

`RootNodeId != null` tek başına okunabilirlik kriteri değildir.

---

## 3.2 StoryNode

Alanlar:

```text
Id
StoryId
PartNumber
Title
IsEnding
CreatedAt
UpdatedAt
```

StoryNode üzerinde:

```text
Text
Momentum
NodeType
```

OLMAYACAK.

Text, Scene tablosunda tutulur.

---

## 3.3 StoryNodeScene

Alanlar:

```text
Id
StoryNodeId
SortOrder
Text
ImageObjectKey?
CreatedAt
UpdatedAt
```

İlişki:

```text
StoryNode 1 --- N StoryNodeScene
```

Index:

```text
(StoryNodeId, SortOrder)
```

---

## 3.4 StoryNodeMemory

Alanlar:

```text
Id
StoryNodeId
Summary
MainCharacterId
CurrentRegionId?
CurrentGoal?
CompanionCharacterIdsJson
ActiveObjectIdsJson
OpenThreadsJson
ImportantEventsJson
EstablishedFactsJson
NarrativeAngleUsed?
CreatedAt
UpdatedAt
```

İlişki:

```text
StoryNode 1 --- 0..1 StoryNodeMemory
```

`StoryNodeId` unique olmalıdır.

Bu veri AI generation/internal state içindir.

Public reading response'ta dönülmez.

---

## 3.5 StoryTransition

Alanlar:

```text
Id
FromNodeId
ToNodeId?
ChoiceTitle
ChoiceIntent
NextTargetMomentum
MeetNewCharacter
ChangeRegion
IntroduceNewObject
IncludeEducation
SortOrder
```

`ToNodeId` BİLEREK nullable'dır.

Sebep:

AI story generation sırasında transition hedef node oluşturulmadan önce üretilebilir.

Navigation:

```text
StoryNode FromNode
StoryNode? ToNode
```

olmalıdır.

Reading tarafı:

```text
ToNodeId == null
```

olan transition'ı kullanıcıya choice olarak sunmamalıdır.

Index:

```text
(FromNodeId, SortOrder)
ToNodeId
```

Generation metadata:

```text
ChoiceIntent
NextTargetMomentum
MeetNewCharacter
ChangeRegion
IntroduceNewObject
IncludeEducation
```

mobile API response'a gönderilmez.

---

# Faz 4 — Reading Domain

## 4.1 StoryReadingSession

Alanlar:

```text
Id
UserId
StoryId
CurrentNodeId
CurrentStep
Status
StartedAt
LastReadAt
CompletedAt?
```

Yeni session:

```text
CurrentStep = 0
```

ile başlar.

`StoryReadingStatus`:

```text
InProgress
Completed
Abandoned
```

---

## 4.2 StoryReadingHistory

Alanlar:

```text
Id
ReadingSessionId
StepNumber
FromNodeId
TransitionId
ToNodeId
SelectedAt
```

Index:

```text
UNIQUE (ReadingSessionId, StepNumber)
```

Semantik:

```text
RootNode
CurrentStep = 0

Choice #1
StepNumber = 1
CurrentStep = 1

Choice #2
StepNumber = 2
CurrentStep = 2
```

Back sırasında history hemen silinmez.

Kullanıcı Back yaptıktan sonra farklı seçim yaparsa:

```text
StepNumber > CurrentStep
```

olan future branch kayıtları silinir.

Event sourcing oluşturma.

---

# Faz 5 — EF Core ve Database

## Amaç

Domain modeli için temiz relational schema oluştur.

## AppDbContext

Gerekli DbSet'ler:

```text
Themes
StoryUniverses
Regions
Characters
StoryObjects
EducationFacts
Stories
StoryNodes
StoryNodeScenes
StoryNodeMemories
StoryTransitions
StoryReadingSessions
StoryReadingHistories
```

Auth tabloları Auth fazında eklenecek.

`StoryTypePartPlans` OLMAYACAK.

## Fluent Configuration

Kritik ilişkiler explicit configure edilmelidir:

```text
Theme -> StoryUniverses
StoryUniverse -> Stories

Story -> Nodes
Story -> RootNode

StoryNode -> Scenes
StoryNode -> Memory

StoryNode -> OutgoingTransitions
StoryNode -> IncomingTransitions

ReadingSession -> Story
ReadingSession -> CurrentNode
ReadingSession -> Histories
```

## SQL Server Delete Behavior

Multiple cascade path oluşturmamaya dikkat et.

Önerilen başlangıç:

```text
Story -> Nodes                 Cascade
Story -> RootNode              NoAction

Node -> Scenes                 Cascade
Node -> Memory                 Cascade

Transition.FromNode            Cascade
Transition.ToNode              NoAction

ReadingSession -> History      Cascade
ReadingSession -> Story        NoAction
ReadingSession -> CurrentNode  NoAction
```

Migration üretildikten sonra SQL Server üzerinde doğrula.

## Indexler

Başlangıç için:

```text
StoryUniverse(ThemeId, IsActive)
Story(UniverseId, Status)
StoryNode(StoryId)
StoryNodeScene(StoryNodeId, SortOrder)
StoryTransition(FromNodeId, SortOrder)
StoryTransition(ToNodeId)
StoryReadingSession(UserId, Status)
StoryReadingSession(UserId, StoryId)
StoryReadingHistory(ReadingSessionId, StepNumber) UNIQUE
```

Sırf "best practice" diye gereksiz index ekleme.

## Initial Migration

Eski database'e uyum sağlama.

Eski migration'ları taşıma.

Tek temiz migration:

```text
InitialCreate
```

oluştur.

Yeni database kullan.

Örnek isim:

```text
StoryAppDb
```

Connection string secret/config üzerinden gelsin.

Source code içine credential yazma.

---

# Faz 6 — API Foundation

## Amaç

Feature geliştirmeden önce API hata ve response altyapısını düzelt.

## Global Exception Handling

ASP.NET Core ProblemDetails kullan.

Her controller/service metoduna try/catch ekleme.

Teknik exception:

```text
500
```

Business validation:

```text
400 / 404 / 409
```

duruma göre map edilir.

Internal exception message client'a dönülmez.

## Validation

Başlangıçta basit validation için ekstra package zorunlu değil.

Request boundary'de:

- empty Guid
- required string
- invalid range

kontrolleri yapılır.

Validation büyürse FluentValidation ayrıca değerlendirilir.

## Time

DB tarihleri UTC.

```text
DateTime.UtcNow
```

veya gerekirse .NET `TimeProvider`.

Sırf abstraction olsun diye custom clock oluşturma.

---

# Faz 7 — Guest Authentication

## Amaç

Mobil uygulama login olmadan kullanılabilsin fakat kullanıcının reading progress'i backend'de güvenli şekilde tutulabilsin.

## Ana Karar

Guest kullanıcı backend açısından gerçek bir User kaydıdır.

Ayrı:

```text
GuestUser
```

tablosu oluşturma.

Tek User modeli kullan.

Hesap tipi:

```text
Guest
Registered
```

olarak ayrılabilir.

## Auth Teknolojisi

ASP.NET Core Identity + Guid key kullanılması tercih edilir.

Sebep:

- password hashing
- user lifecycle
- login provider desteği
- registered account upgrade
- güvenlik altyapısını sıfırdan yazmamak

Guest kullanıcı Identity user kaydı olarak oluşturulabilir.

Guest için email/password zorunlu değildir.

Identity configuration bunu destekleyecek şekilde açıkça ayarlanmalıdır.

Gereksiz Identity UI veya Razor Pages ekleme.

---

## RefreshToken

Yeni entity minimum:

```text
Id
UserId
TokenHash
CreatedAt
ExpiresAt
RevokedAt?
ReplacedByTokenId?
```

Opsiyonel alanlar gerçek ihtiyaç gelmeden eklenmez.

Başlangıçta EKLEME:

```text
DeviceName
Platform
IpAddress
AppVersion
```

gerekiyorsa daha sonra eklenir.

Raw refresh token DB'ye yazılmaz.

Sadece hash saklanır.

---

## Endpointler

```http
POST /api/auth/guest
POST /api/auth/refresh
POST /api/auth/logout
```

### POST /api/auth/guest

Backend:

1. Guest User oluşturur.
2. Access token üretir.
3. Refresh token üretir.
4. Refresh token hash'ini DB'ye yazar.
5. Tokenları client'a döner.

Client refresh token'ı secure storage'da saklar:

```text
iOS Keychain
Android Keystore / secure storage
```

Hardware device id'yi auth identity olarak kullanma.

### Refresh

- expired token reddedilir
- revoked token reddedilir
- rotation uygulanır
- kullanılan token revoke edilir
- yeni refresh token oluşturulur

### Logout

İlgili refresh token revoke edilir.

---

# Faz 8 — Current User Context

Protected endpoint'lerde client'tan:

```text
UserId
```

alma.

User identity JWT claim'den okunur.

Route tasarımı:

```text
/api/me
/api/reading-sessions
```

gibi olmalıdır.

Gerekirse küçük:

```text
ICurrentUser
```

abstraction eklenebilir.

Ancak claim parsing tek yerdeyse ekstra abstraction oluşturma.

## Endpoint

```http
GET /api/me
```

Minimum response:

```json
{
  "id": "guid",
  "accountType": "Guest"
}
```

---

# Faz 9 — Theme API

## Endpoint

```http
GET /api/themes
```

Sadece:

```text
IsActive == true
```

Theme'ler dönülür.

Sıralama:

```text
SortOrder ASC
Name ASC
```

Entity dönme.

Response:

```text
id
name
description
icon
color
sortOrder
```

Query:

```text
AsNoTracking
Projection
```

---

# Faz 10 — Reading Response Model

Mobil client tek response ile node ekranını render edebilmeli.

Örnek logical response:

```text
ReadingSessionResponse
  SessionId
  StoryId
  StoryTitle
  Status
  CanGoBack
  CurrentNode
```

CurrentNode:

```text
Id
Title
IsEnding
Scenes[]
Choices[]
```

Scene:

```text
Id
SortOrder
Text
ImageObjectKey
```

Choice:

```text
TransitionId
Title
SortOrder
```

Mobile'a DÖNME:

```text
StoryNodeMemory
ChoiceIntent
NextTargetMomentum
MeetNewCharacter
ChangeRegion
IntroduceNewObject
IncludeEducation
FailureReason
generation prompt data
```

Story'nin tüm graph'ını yükleme.

Sadece current node + scenes + usable outgoing transitions.

---

# Faz 11 — Play / Story Selection

## Endpoint

```http
POST /api/play
```

Request:

```json
{
  "themeId": "guid"
}
```

## Flow

1. JWT üzerinden User al.
2. Theme active mı kontrol et.
3. Theme altındaki active Universe adaylarını bul.
4. Uygun Universe seç.
5. Universe altındaki Story adaylarında:

```text
Status == Ready
RootNodeId != null
```

zorunlu olsun.

6. Age/language bilgisi mevcutsa filtreye dahil et.
7. Uygun Story seç.
8. Mümkünse kullanıcının yakın zamanda tamamladığı Story'yi öncelikle dışla.
9. Aday kalmazsa tekrar okunmasına izin ver.
10. ReadingSession oluştur:

```text
CurrentNodeId = Story.RootNodeId
CurrentStep = 0
Status = InProgress
```

11. Root node response dön.

## Random Selection

İlk sürümde bütün Story listesini memory'ye alma.

Basit SQL-friendly yaklaşım:

```text
filtered count
random index
stable OrderBy
Skip(index)
First
```

Dataset büyürse ayrıca optimize edilir.

---

# Faz 12 — Choice

## Endpoint

```http
POST /api/reading-sessions/{sessionId}/choices
```

Request:

```json
{
  "currentNodeId": "guid",
  "transitionId": "guid"
}
```

Client `ToNodeId` göndermez.

Backend Transition üzerinden target belirler.

## Validation

Zorunlu:

```text
session.UserId == authenticated UserId
session.Status uygun
session.CurrentNodeId == request.CurrentNodeId
transition.FromNodeId == session.CurrentNodeId
transition.ToNodeId != null
transition aynı Story graph'ına ait
```

Stale node:

```http
409 Conflict
```

## Transaction

Tek transaction içinde:

1. Session doğrula.
2. Transition doğrula.
3. `StepNumber > CurrentStep` future history varsa sil.
4. Yeni history ekle:

```text
StepNumber = CurrentStep + 1
FromNodeId
TransitionId
ToNodeId
SelectedAt
```

5. Session güncelle:

```text
CurrentStep++
CurrentNodeId = ToNodeId
LastReadAt = UtcNow
```

6. Target ending ise:

```text
Status = Completed
CompletedAt = UtcNow
```

7. Save.
8. Yeni current node response dön.

Mobil double tap/stale request bu modelle kontrol edilir.

---

# Faz 13 — Back

## Endpoint

```http
POST /api/reading-sessions/{sessionId}/back
```

## Flow

1. Ownership kontrol.
2. `CurrentStep > 0`.
3. `StepNumber == CurrentStep` history kaydını bul.
4. `CurrentNodeId = history.FromNodeId`.
5. `CurrentStep--`.
6. `LastReadAt` güncelle.
7. Completed ise:

```text
Status = InProgress
CompletedAt = null
```

8. Save.
9. Current node response dön.

Back sırasında history SİLME.

Kullanıcı başka seçim yaptığında future branch silinir.

---

# Faz 14 — Reading Session / Okunan Hikâyeler

## Endpointler

```http
GET /api/reading-sessions
GET /api/reading-sessions/{sessionId}
GET /api/me/stories
```

Ownership her zaman JWT user üzerinden.

Başka kullanıcıya ait session açılmamalı.

Liste büyüyebileceği için pagination kullan.

Minimum history/list response:

```text
sessionId
storyId
storyTitle
status
startedAt
lastReadAt
completedAt
```

Aynı Story birden fazla okunabiliyorsa session bazlı model korunur.

---

# Faz 15 — Profile / Settings

UI:

```text
Profile
Settings
Play
```

Server'a ait ve local setting ayrımı yap.

Server-side adaylar:

```text
child age
preferred language
content preferences
parental settings
```

Local mobile adaylar:

```text
dark mode
sound
animation
local UI preferences
```

Ürün kararı olmadan generic Settings tablosu oluşturma.

---

# Faz 16 — Registered Account / Guest Upgrade

Guest kullanıcı hikâye okuduktan sonra register olabilmelidir.

Tercih:

```text
aynı UserId korunur
```

Guest user yeni registered user'a data copy/merge ile taşınmaz.

Mevcut user kaydı upgrade edilir.

Böylece:

```text
ReadingSession
ReadingHistory
Completed Stories
```

aynı UserId altında kalır.

İlk registered auth yöntemi ürün kararına göre seçilir:

```text
email/password
Apple
Google
```

Hepsini aynı anda implement etme.

---

# Faz 17 — Abandon / Restart

Gerçek UX ihtiyacı geldikten sonra implement et.

Abandon:

```http
POST /api/reading-sessions/{sessionId}/abandon
```

Restart için tercih:

```text
yeni ReadingSession oluştur
```

olabilir.

Ancak ürün kararı gelmeden endpoint ekleme.

---

# Faz 18 — AI Story Generation

Reading/Auth akışı stabil olduktan sonra generation pipeline yeniden kurulur.

Bu fazda mevcut Story graph modeli kullanılmalıdır.

Generation sonucu:

```text
Story.Status = Generating
```

ile başlar.

Başarılı generation:

```text
Story.Status = Ready
Story.RootNodeId != null
CompletedAt = UtcNow
```

Başarısız generation:

```text
Story.Status = Failed
FailureReason = internal diagnostic
```

Partial graph bulunabilir.

Reading sistemi Failed/Generating Story'leri asla seçmez.

## Generation Metadata

Aşağıdaki alanlar generation için kullanılabilir:

```text
StoryNodeMemory
ChoiceIntent
NextTargetMomentum
MeetNewCharacter
ChangeRegion
IntroduceNewObject
IncludeEducation
```

Bunlar public API contract değildir.

## StoryTypePartPlan

Generation yeniden yazılırken de eski `StoryTypePartPlans` tablosunu otomatik geri getirme.

Gerçek ihtiyaç çıkarsa yeni modele göre ayrıca tasarla.

---

# Faz 19 — Hardening

## Rate Limiting

Öncelik:

```text
POST /api/auth/guest
POST /api/auth/refresh
POST /api/auth/login
```

## Concurrency

Choice endpoint double-submit test edilmeli.

İlk savunma:

```text
currentNodeId stale check
transaction
```

Yetersiz kalırsa optimistic concurrency eklenir.

Başlangıçta provider-specific concurrency karmaşıklığı ekleme.

## Logging

Structured context:

```text
UserId
ReadingSessionId
StoryId
NodeId
TransitionId
```

Loglama:

- access token
- refresh token
- password
- story text'in tamamı

yapma.

## Query Performance

- read-only sorgularda `AsNoTracking`
- gerekli projection
- gereksiz Include yok
- N+1 yok
- tüm graph memory'ye alınmaz

---

# Faz 20 — Integration Tests

## Auth

```text
GuestAuth_CreatesUserAndTokens
Refresh_RotatesToken
Refresh_WithExpiredToken_Fails
Refresh_WithRevokedToken_Fails
Logout_RevokesToken
RawRefreshToken_IsNotStored
```

## Theme

```text
GetThemes_ReturnsOnlyActive
GetThemes_OrdersCorrectly
```

## Play

```text
Play_WithInactiveTheme_Fails
Play_SelectsOnlyActiveUniverse
Play_SelectsOnlyReadyStory
Play_DoesNotSelectFailedStoryWithRootNode
Play_CreatesReadingSession
Play_StartsAtRootNode
Play_StartsWithCurrentStepZero
```

## Choice

```text
Choice_MovesToNextNode
Choice_CreatesHistoryWithCorrectStep
Choice_WithWrongFromNode_Fails
Choice_WithPendingTransition_Fails
Choice_WithStaleCurrentNode_ReturnsConflict
Choice_ToEnding_CompletesSession
```

## Back

```text
Back_MovesToPreviousNode
Back_DecrementsCurrentStep
Back_DoesNotImmediatelyDeleteHistory
Back_FromEnding_ReopensSession
Back_AtRoot_FailsCleanly
Rechoose_AfterBack_RemovesFutureBranch
```

## Authorization

```text
User_CannotReadAnotherUsersSession
User_CannotChooseOnAnotherUsersSession
User_CannotBackAnotherUsersSession
```

---

# Faz 21 — İlk Release Minimum Endpoint Set

MVP:

```http
POST /api/auth/guest
POST /api/auth/refresh
POST /api/auth/logout

GET  /api/me

GET  /api/themes
POST /api/play

GET  /api/reading-sessions
GET  /api/reading-sessions/{sessionId}
POST /api/reading-sessions/{sessionId}/choices
POST /api/reading-sessions/{sessionId}/back

GET  /api/me/stories
```
# AI Generation Scope

AI story generation bu backend projesinin sorumluluğunda değildir.

Story generation ayrı bir proje / generation engine içerisinde çalışır.

Bu backend:

- yalnızca hazır Story verisini tüketir
- yalnızca `Story.Status == Ready` olan hikâyeleri Play akışında seçer
- `StoryNodeMemory` ve generation metadata alanlarını mobile API'ye expose etmez
- AI provider çağrısı yapmaz
- prompt oluşturmaz
- story generation pipeline yönetmez

Generation engine tarafından kullanılan ortak Story schema'sı ile uyumluluk korunmalıdır.

Aşağıdaki alanlar generation engine tarafından kullanılabilir:

- StoryNodeMemory
- ChoiceIntent
- NextTargetMomentum
- MeetNewCharacter
- ChangeRegion
- IntroduceNewObject
- IncludeEducation

Bu alanlar StoryApp mobile API contract'ının parçası değildir.
---

# Geliştirme Sırası Özeti

```text
1.  Solution Bootstrap
2.  Domain Foundation
3.  Story Graph
4.  Reading Domain
5.  EF Core + Clean Initial Database
6.  API Foundation
7.  Guest Auth
8.  Current User
9.  Themes
10. Reading Response
11. Play
12. Choice
13. Back
14. Reading Lists / Resume
15. User Profile + Age/Language
16. Registered Upgrade
17. Abandon / Restart
18. Hardening
19. Integration Tests
20. MVP Release
```

Her faz tamamlandıktan sonra dur.

Sonraki faza otomatik geçme.

Her faz sonunda:

```text
- değişen/eklenen dosyalar
- migration durumu
- config değişiklikleri
- build sonucu
- test sonucu
- açık noktalar
```

raporlanmalıdır.

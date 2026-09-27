# StoryApp Backend — Codex Architecture & Design Guide

> Bu doküman Codex'in StoryApp backend üzerinde kod üretirken uyması gereken teknik ve mimari kuralları tanımlar.
> Amaç: production'a uygun, basit, maintainable ve mevcut projeyle uyumlu ASP.NET Core Web API geliştirmek.

---

## 1. Proje Özeti

StoryApp, kullanıcının seçimlerine göre dallanan interaktif hikâyeler okuduğu bir mobil uygulamadır.

Temel domain akışı:

```text
Theme
  -> StoryUniverse
      -> Story
          -> StoryNode
              -> StoryNodeScene
              -> StoryTransition
                  -> StoryNode
```

Okuma tarafı:

```text
User
  -> StoryReadingSession
      -> StoryReadingHistory
```

Kullanıcı:

1. Uygulamayı login olmadan açabilir.
2. Play'e basar.
3. Theme seçer.
4. Backend seçilen Theme altında uygun bir StoryUniverse seçer.
5. Backend bu Universe içerisinden uygun bir Story seçer.
6. ReadingSession başlatılır veya uygun durumda devam ettirilir.
7. Kullanıcı StoryNode içeriğini okur.
8. Bir StoryTransition seçer.
9. Session yeni StoryNode'a ilerler.
10. Kullanıcı isterse önceki seçime geri dönebilir.
11. Ending node'a gelirse session tamamlanır.

---

# 2. Ana Teknik Stack

- C#
- ASP.NET Core Web API
- REST
- Entity Framework Core
- Relational database
- async/await
- Dependency Injection
- JWT Bearer authentication
- ASP.NET Core Identity kullanılabiliyorsa registered ve guest kullanıcı kimliği için aynı user modeli tercih edilir
- Global exception handling / ProblemDetails
- CancellationToken
- Production-ready configuration

Project mevcutta farklı bir auth veya persistence standardına sahipse mevcut standardı koru.

---

# 3. Temel Mimari Prensip

Yeni ve gereksiz mimari katmanlar üretme.

Tercih edilen basit akış:

```text
Controller
   |
   v
Application / Service
   |
   v
EF Core DbContext
   |
   v
Database
```

Eğer mevcut projede Repository katmanı zaten varsa onu kullan.

Eğer yoksa sadece "best practice" gerekçesiyle generic repository oluşturma.

EF Core `DbContext` zaten Repository + Unit of Work davranışının önemli kısmını sağlar.

Şunları otomatik oluşturma:

- GenericRepository<T>
- BaseService<T>
- UnitOfWork wrapper
- CQRS/MediatR
- EventBus
- Domain event altyapısı
- Specification Pattern

Bunlardan biri gerçek bir ihtiyacı çözüyorsa ayrıca değerlendir.

---

# 4. SOLID Yaklaşımı

SOLID uygulanacak fakat abstraction uğruna abstraction yapılmayacak.

## Single Responsibility

Controller:

- HTTP request alır.
- Request validation sonucu ile çalışır.
- Service çağırır.
- HTTP response üretir.

Controller içinde:

- EF query yazma.
- Story selection algoritması yazma.
- Reading state değiştirme.
- Refresh token üretme.
- Business rule uygulama.

Service:

- Business flow yönetir.
- DbContext ile gerekli query/update işlemlerini yapar.
- Transaction sınırlarını belirler.

## Open/Closed

Gelecekte Story selection algoritması karmaşıklaşırsa ayrı strategy/service çıkarılabilir.

İlk sürümde sırf gelecekte gerekebilir diye strategy abstraction oluşturma.

## Liskov

Gerçek inheritance ihtiyacı yoksa inheritance kullanma.

## Interface Segregation

Sadece DI veya test sınırı gerektiğinde interface oluştur.

Örnek:

```csharp
IStoryReadingService
IPlayService
ITokenService
```

Her entity için ayrı service interface oluşturma.

## Dependency Inversion

Controller concrete implementation yerine service abstraction'a bağımlı olabilir.

Infrastructure dependency'leri service'e DI ile verilir.

---

# 5. DTO ve Entity Ayrımı

Entity hiçbir zaman doğrudan API response olarak dönülmemelidir.

Yanlış:

```csharp
return Ok(storyNode);
```

Doğru:

```csharp
return Ok(new StoryNodeResponse(...));
```

Request ve Response modelleri domain entity'lerinden ayrı tutulur.

Küçük mapping işlemlerinde manual mapping tercih edilir.

Sadece mapping ciddi şekilde büyürse AutoMapper gibi ek dependency değerlendirilir.

---

# 6. Authentication Tasarımı

## 6.1 Hedef

Mobil uygulama login gerektirmeden çalışmalıdır.

Ancak login olmadan:

- reading session
- reading history
- completed stories
- progress
- settings/profile state

kullanıcıyla ilişkilendirilebilmelidir.

## 6.2 Guest Account Modeli

İlk uygulama açılışında client:

```http
POST /api/auth/guest
```

çağrısı yapar.

Backend:

1. Yeni guest user oluşturur.
2. Access token üretir.
3. Refresh token üretir.
4. Refresh token'ın raw değerini DB'ye yazmaz.
5. Refresh token hash'ini saklar.
6. Token bilgilerini client'a döner.

Client refresh token'ı iOS Keychain / Android Keystore gibi secure storage içinde saklar.

Telefonun hardware device id değerini authentication kimliği olarak kullanma.

## 6.3 User Kimliği

Protected endpoint'lerde UserId request body veya route'tan alınmamalıdır.

UserId JWT claim'den çıkarılmalıdır.

Örnek:

```text
/api/me/...
/api/reading-sessions/...
```

`/api/users/{userId}/...` ancak admin veya başka kullanıcıyı görüntüleme gibi gerçek bir ihtiyaç varsa kullanılmalıdır.

## 6.4 Guest -> Registered Upgrade

Kullanıcı daha sonra register/login olmak isterse mümkün olduğunda mevcut guest `UserId` korunmalıdır.

Önerilen yaklaşım:

```text
Guest User
   |
   + add email/password/external-login
   |
Registered User
```

Böylece:

- StoryReadingSession taşınmaz.
- History taşınmaz.
- Completed stories taşınmaz.

Hepsi aynı UserId üzerinde kalır.

Eğer auth provider nedeniyle aynı user kaydını upgrade etmek mümkün değilse explicit merge transaction tasarlanmalıdır.

## 6.5 Token Stratejisi

Access token:

- short-lived
- JWT Bearer

Refresh token:

- long-lived
- cryptographically random
- DB'de hash saklanır
- rotation uygulanır
- logout/revoke desteklenir

Config değerleri source code içinde hard-code edilmez.

Örnek config:

```text
Jwt:Issuer
Jwt:Audience
Jwt:SigningKey
Jwt:AccessTokenMinutes
Jwt:RefreshTokenDays
```

Signing key environment variable / secret store üzerinden gelmelidir.

## 6.6 Auth Endpointleri

İlk sürüm:

```http
POST /api/auth/guest
POST /api/auth/refresh
POST /api/auth/logout
```

Registered account özelliği geldiğinde:

```http
POST /api/auth/register
POST /api/auth/login
POST /api/auth/upgrade
```

Endpoint isimleri mevcut proje convention'ına göre değişebilir.

---

# 7. Story Selection / Play Tasarımı

Client'ın şu işlemleri ayrı ayrı yapması önerilmez:

```text
Theme getir
Universe seç
Story seç
Session oluştur
Root node getir
```

Client sadece Theme seçimini bildirir.

Backend orchestration yapar.

Önerilen endpoint:

```http
POST /api/play
```

Request:

```json
{
  "themeId": "guid"
}
```

Backend:

1. Theme aktif mi kontrol eder.
2. Kullanıcı profilinden age/language bilgisi varsa filtreye dahil eder.
3. Aktif universe adaylarını bulur.
4. Uygun universe seçer.
5. `StoryStatus.Ready` hikâyeler arasından uygun Story seçer.
6. Mümkünse yakın zamanda tamamlanan hikâyeyi tekrar seçmez.
7. ReadingSession oluşturur.
8. Story.RootNode üzerinden ilk response'u üretir.

Response mümkün olduğunca mobil uygulamanın direkt render edebileceği shape'te olmalıdır.

---

# 8. Story Reading API Tasarımı

Önerilen API:

```http
GET  /api/themes

POST /api/play

GET  /api/reading-sessions
GET  /api/reading-sessions/{sessionId}
POST /api/reading-sessions/{sessionId}/choices
POST /api/reading-sessions/{sessionId}/back
POST /api/reading-sessions/{sessionId}/abandon

GET  /api/me/stories
```

`POST /choices` örnek request:

```json
{
  "currentNodeId": "guid",
  "transitionId": "guid"
}
```

`currentNodeId` özellikle gönderilmelidir.

Amaç:

- stale screen kontrolü
- double tap riskini azaltma
- client'ın eski node üzerinden transition seçmesini engelleme

Backend mutlaka doğrulamalıdır:

```text
session.UserId == authenticatedUserId
session.Status uygun
session.CurrentNodeId == request.CurrentNodeId
transition.FromNodeId == session.CurrentNodeId
transition Story'ye ait
transition.ToNodeId geçerli
```

Client'tan gelen `ToNodeId` hiçbir zaman güvenilir input olarak alınmamalıdır.

Next node backend tarafından Transition üzerinden bulunmalıdır.

---

# 9. Reading History ve Back Tasarımı

Mevcut modelde `StoryReadingHistory` var. Bu doğru bir yaklaşımdır.

Ancak back/re-choose için history üzerinde sıralı step bilgisi gerekir.

Öneri:

```csharp
public int StepNumber { get; set; }
```

`StoryReadingSession` içine:

```csharp
public int CurrentStep { get; set; }
```

eklenmesi önerilir.

## Normal seçim

Örnek:

```text
CurrentStep = 3
```

Yeni seçim:

```text
StepNumber = 4
CurrentStep = 4
CurrentNodeId = transition.ToNodeId
```

## Back

CurrentStep 4 ise:

1. Step 4 history kaydı bulunur.
2. Session.CurrentNodeId = History.FromNodeId
3. Session.CurrentStep = 3

## Back sonrası farklı seçim

Kullanıcı geçmişe dönüp başka seçim yaparsa:

```text
History where StepNumber > CurrentStep
```

aktif path'ten çıkarılmalıdır.

İlk sürüm için en basit çözüm:

- future history satırlarını sil
- yeni branch üzerinden history üret

Abandoned branch analytics ileride gerekiyorsa append-only event log ayrıca tasarlanabilir.

Şimdilik bunu sisteme ekleme.

---

# 10. Story Completion

StoryNode `IsEnding == true` ise node'a geçildiğinde:

```text
StoryReadingSession.Status = Completed
CompletedAt = UtcNow
```

Kullanıcı ending'den Back yapabiliyorsa:

```text
Status = InProgress
CompletedAt = null
```

olmalıdır.

`StoryReadingStatus` enum en az aşağıdaki durumları desteklemelidir:

```text
InProgress
Completed
Abandoned
```

Mevcut enum farklıysa mevcut yapıya uy.

---

# 11. Domain Model Üzerindeki Önemli Düzeltme Önerileri

## 11.1 StoryTransition.ToNode

Mevcut yapı:

```csharp
public Guid? ToNodeId { get; set; }
public StoryNode ToNode { get; set; } = null!;
```

Burada nullability tutarsızdır.

Story ending ayrı bir `StoryNode` olarak tutuluyorsa Transition her zaman bir node'a gitmelidir.

Bu durumda tercih:

```csharp
public Guid ToNodeId { get; set; }
public StoryNode ToNode { get; set; } = null!;
```

Sadece gerçek bir "transition doğrudan biter, ending node yoktur" kuralı varsa nullable bırak.

Mevcut domain model ending node kullandığı için required olması daha tutarlıdır.

## 11.2 StoryReadingSession Story type

Gönderilen örnekte:

```csharp
public StoryEntity Story { get; set; } = null!;
```

fakat story entity:

```csharp
public sealed class Story : BaseEntity
```

olarak tanımlı.

Gerçek projede `StoryEntity` yoksa bu compile hatasıdır ve `Story` olmalıdır.

## 11.3 EducationFact.Region

`RegionId` nullable ise navigation da nullable olmalıdır:

```csharp
public Region? Region { get; set; }
```

## 11.4 StoryNodeType ve IsEnding

Şu an hem:

```csharp
StoryNodeType
```

enum'u var hem de:

```csharp
bool IsEnding
```

kullanılıyor.

İlk sürüm için tek source of truth kullan.

Mevcut kod `IsEnding` ile çalışıyorsa enum'u henüz modele ekleme.

Start node zaten `Story.RootNodeId` ile belirlenebilir.

## 11.5 BaseEntity tutarlılığı

Aşağıdaki entity'lerde BaseEntity kullanılmıyor:

- StoryTransition
- StoryReadingSession
- StoryReadingHistory

Bu bilinçli değilse audit alanları/naming açısından standardize edilmeli.

Ancak BaseEntity içeriğini görmeden Codex bunu değiştirmemelidir.

## 11.6 RootNode relationship

`Story.RootNodeId -> StoryNode`
ve
`StoryNode.StoryId -> Story`

çift yönlü ilişki oluşturur.

EF configuration explicit yapılmalıdır.

Cascade delete burada dikkatli kullanılmalıdır.

RootNode foreign key için `Restrict` / `NoAction` tercih edilmesi çoğu relational provider için daha güvenlidir.

---

# 12. EF Core Configuration Kuralları

Fluent configuration tercih et.

Önemli ilişkiler explicit tanımlanmalıdır.

## StoryNode -> StoryTransition

```text
StoryNode.OutgoingTransitions
StoryTransition.FromNode

StoryNode.IncomingTransitions
StoryTransition.ToNode
```

İki FK explicit configure edilmelidir.

## One-to-one StoryNodeMemory

```text
StoryNode
  1 --- 0..1 StoryNodeMemory
```

`StoryNodeMemory.StoryNodeId` unique olmalıdır.

## Index önerileri

Kesin ihtiyaç:

```text
StoryNode(StoryId)
StoryTransition(FromNodeId)
StoryTransition(ToNodeId)
StoryReadingSession(UserId, Status)
StoryReadingSession(UserId, StoryId)
StoryReadingHistory(ReadingSessionId, StepNumber)
Story(UniverseId, Status)
StoryUniverse(ThemeId, IsActive)
```

Muhtemel unique indexler:

```text
StoryNodeScene(StoryNodeId, SortOrder)
StoryTransition(FromNodeId, SortOrder)
Region(StoryUniverseId, Code)
Character(StoryUniverseId, Code)
StoryObject(StoryUniverseId, Code)
```

Business rule doğrulanmadan gereksiz unique constraint ekleme.

---

# 13. Delete Behavior

Cascade delete otomatik kabul edilmemelidir.

Özellikle:

```text
Story -> Nodes
Node -> Transitions
Story -> RootNode
ReadingSession -> History
```

ilişkileri incelenmelidir.

ReadingSession -> History için cascade mantıklı olabilir.

Story graph tarafında çoklu cascade path riskinden dolayı explicit `Restrict` / `NoAction` gerekebilir.

Database provider görülmeden migration üretirken varsayım yapma.

---

# 14. Query Kuralları

Read-only query:

```csharp
.AsNoTracking()
```

gerektiğinde kullanılmalıdır.

Navigation lazım değilse `Include` yapma.

API response için çoğu durumda projection tercih et:

```csharp
.Select(x => new Response(...))
```

Graph'ın tamamını memory'ye alma.

Örneğin current node response üretmek için:

- current node
- ordered scenes
- outgoing transitions

yeterlidir.

Story'nin bütün node graph'ını yükleme.

---

# 15. Random Story Selection

İlk sürümde data hacmi küçükse basit ve anlaşılır çözüm seç.

Ancak bütün Story kayıtlarını memory'ye alıp sonra random seçme.

Örnek yaklaşım:

1. filtered query count
2. random index
3. deterministic `OrderBy`
4. `Skip(index).Take(1)`

Daha sonra dataset büyürse selection algoritması optimize edilebilir.

`ORDER BY NEWID()` gibi provider-specific çözümü varsayılan hale getirme.

---

# 16. Concurrency ve Double Tap

Mobil kullanıcı aynı choice butonuna iki kez basabilir.

Service:

- session current node'u doğrulamalı
- transition FromNode'u doğrulamalı
- history insert + session update aynı transaction içinde yapılmalı

Gerekirse EF optimistic concurrency token kullanılmalıdır.

Database provider ve `BaseEntity` görülmeden provider-specific `rowversion/xmin` kodu ekleme.

İlk adımda stale `currentNodeId` doğrulaması zorunludur.

---

# 17. Transaction Sınırları

Aşağıdaki işlem tek transaction olmalıdır:

```text
Validate current session
Validate transition
Remove future history if rewound
Insert StoryReadingHistory
Update CurrentNodeId
Update CurrentStep
Update LastReadAt
Update completion status
Save
```

Benzer şekilde guest->registered merge gerekiyorsa transaction kullanılmalıdır.

---

# 18. Validation

Request validation boundary'de yapılmalıdır.

Kontrol örnekleri:

- Guid empty olamaz
- ThemeId mevcut ve active olmalı
- Session authenticated user'a ait olmalı
- Transition current node'a ait olmalı
- Story Ready olmalı
- Age filters geçerli olmalı
- Language desteklenmeli

Business validation ile input validation ayrılmalıdır.

Her service metodunu try/catch ile doldurma.

Global exception handling varsa teknik exception oraya bırakılmalıdır.

---

# 19. Error Contract

Tercih:

```text
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
422 ancak mevcut API standardı kullanıyorsa
500 Internal Server Error
```

Stale current node gibi state conflict durumunda:

```http
409 Conflict
```

mantıklıdır.

ProblemDetails kullanılabiliyorsa kullan.

Internal exception message client'a dönülmemelidir.

---

# 20. Time Handling

Database timestamp'leri UTC saklanmalıdır.

Kullan:

```csharp
DateTime.UtcNow
```

veya mevcut projede `TimeProvider` abstraction varsa onu kullan.

Yeni abstraction sırf bunun için ekleme.

---

# 21. API Response — Current Reading Node

Mobil client'ın tek response ile ekranı çizebilmesi hedeflenmelidir.

Örnek:

```json
{
  "sessionId": "guid",
  "storyId": "guid",
  "storyTitle": "Example",
  "currentNode": {
    "id": "guid",
    "title": "Chapter",
    "isEnding": false,
    "scenes": [
      {
        "id": "guid",
        "sortOrder": 1,
        "text": "...",
        "imageObjectKey": "..."
      }
    ],
    "choices": [
      {
        "transitionId": "guid",
        "title": "Kapıyı aç",
        "sortOrder": 1
      }
    ]
  },
  "canGoBack": true,
  "status": "InProgress"
}
```

Response içinde:

- internal memory
- ChoiceIntent
- generation control fields
- NextTargetMomentum
- AI prompt metadata

client'a gerekmiyorsa dönülmemelidir.

---

# 22. İçerik Üretim Alanları ve Public API Ayrımı

`StoryTransition` içindeki:

```text
ChoiceIntent
NextTargetMomentum
MeetNewCharacter
ChangeRegion
IntroduceNewObject
IncludeEducation
```

alanları generation orchestration alanları gibi görünüyor.

Reading endpoint response'unda yalnızca kullanıcıya gerekli seçim bilgisi dönmelidir.

Örneğin:

```text
transitionId
choiceTitle
sortOrder
```

Generation metadata public mobile contract'a sızdırılmamalıdır.

Aynı prensip `StoryNodeMemory` için de geçerlidir.

`StoryNodeMemory` public read endpoint'lerinde dönülmemelidir.

---

# 23. Security

- Raw SQL gerekiyorsa parameterized kullan.
- Client'tan gelen UserId'ye güvenme.
- Story session ownership her request'te kontrol edilmeli.
- Refresh token DB'de raw saklanmamalı.
- JWT signing key source code'a konmamalı.
- Rate limiting auth endpoint'lerinde değerlendirilmelidir.
- Log'lara token veya secret yazılmamalıdır.
- User authorization sadece route'un varlığına bırakılmamalıdır.

---

# 24. Logging

Log'larda yararlı context:

```text
UserId
ReadingSessionId
StoryId
CurrentNodeId
TransitionId
```

Ancak story text'in tamamını veya token'ları loglama.

Expected business conflict `Error` seviyesinde loglanmamalıdır.

---

# 25. Testing Öncelikleri

Unit test veya integration test altyapısı mevcutsa şu senaryolar öncelikli:

1. guest auth creates usable identity
2. refresh token rotation
3. user cannot access another user's session
4. invalid transition is rejected
5. stale currentNodeId returns conflict
6. choice updates current node
7. choice writes history
8. ending marks session completed
9. back moves to previous node
10. back from ending reopens session
11. re-choose after back removes obsolete future path
12. unavailable/inactive theme cannot start play
13. only Ready stories are selected

API integration tests, service-only unit testlerden daha değerlidir çünkü EF relationships ve auth ownership da doğrulanır.

---

# 26. Codex İçin Kod Yazma Kuralları

Codex her görevde:

1. Önce mevcut dosyaları incele.
2. Mevcut namespace'i kullan.
3. BaseEntity içeriğini görmeden varsayım yapma.
4. DbContext/configuration yapısını incele.
5. Authentication altyapısı varsa yeniden kurma.
6. Generic repository ekleme.
7. Entity'yi response olarak dönme.
8. Gereksiz package ekleme.
9. Public API contract değişiyorsa belirt.
10. Migration gerektiren değişiklikleri açıkça listele.
11. Compile hatası oluşturacak hayali class/namespace üretme.
12. Bir değişiklik birkaç satırsa tüm projeyi refactor etme.
13. Kullanıcı istemedikçe mevcut çalışan naming'i topluca değiştirme.
14. Nullable reference type kurallarına uy.
15. Async EF metotlarında `CancellationToken` geçir.
16. `SaveChangesAsync(cancellationToken)` kullan.
17. Read query'lerinde gerektiğinde `AsNoTracking()`.
18. Business operation atomik olmalıysa transaction kullan.
19. API ownership'i JWT user üzerinden kontrol et.
20. Kod sonunda değiştirdiği dosyaları ve migration ihtiyacını özetle.

---

# 27. Şimdilik Yapılmaması Gerekenler

Bu aşamada otomatik ekleme:

- microservice
- message broker
- Redis
- distributed cache
- event sourcing
- graph database
- CQRS
- MediatR
- generic repository
- complex recommendation engine
- vector database
- AI agent orchestration
- feature flag platform

İhtiyaç doğarsa sonradan eklenebilir.

Story graph relational database üzerinde mevcut modelle rahatlıkla yürütülebilir.

---

# 28. Mimari Karar Özeti

StoryApp ilk backend sürümünde:

```text
ASP.NET Core Web API
        |
        v
Small focused services
        |
        v
EF Core DbContext
        |
        v
Relational DB
```

Authentication:

```text
Guest User
   |
JWT + Refresh Token
   |
Registered User'a upgrade
```

Reading:

```text
Theme
  -> server selects Universe
  -> server selects Ready Story
  -> ReadingSession
  -> RootNode
  -> Transition
  -> History
  -> Next Node
  -> Ending
```

Ana hedef:

> Basit, güvenli, transactional ve mobil client'ın mümkün olduğunca az orchestration yaptığı bir backend.

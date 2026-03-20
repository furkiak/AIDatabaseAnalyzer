<div align="center">

![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![Clean Architecture](https://img.shields.io/badge/Architecture-MVVM%20%7C%20Clean-success?style=for-the-badge)

![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927?style=for-the-badge&logo=microsoft-sql-server&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white)
![MySQL](https://img.shields.io/badge/MySQL-005C84?style=for-the-badge&logo=mysql&logoColor=white)

![Gemini AI](https://img.shields.io/badge/Gemini%20AI-4285F4?style=for-the-badge&logo=google-gemini&logoColor=white)
![Ollama](https://img.shields.io/badge/Ollama-Local%20AI-000000?style=for-the-badge&logo=ollama&logoColor=white)
![AI Analysis](https://img.shields.io/badge/AI%20Analysis-8A2BE2?style=for-the-badge)

![Performance](https://img.shields.io/badge/Performance%20Tuning-FF8C00?style=for-the-badge)
![Security](https://img.shields.io/badge/Security%20Audit-DC143C?style=for-the-badge)

![QuestPDF](https://img.shields.io/badge/Reporting-QuestPDF-0078D4?style=for-the-badge)
![Mermaid.js](https://img.shields.io/badge/Visualization-Mermaid.js-FF3670?style=for-the-badge)

# AI Database Analyzer & Architect

*An autonomous, AI-driven Senior Database Consultant in your pocket.*

<p align="center">
  <img src="https://raw.githubusercontent.com/furkiak/AIDatabaseAnalyzer/refs/heads/main/SSD.png" width="600" title="ActiveRest Dashboard">
</p>

 

</div>

---

# 🇺🇸 English Version

## The Vision
In the modern era of software engineering, database schemas often become "black boxes" accumulating technical debt, hidden bottlenecks, and security vulnerabilities. **AI Database Analyzer** is an enterprise-grade architectural intelligence tool designed to bridge the gap between raw database schemas and actionable expert insights. 

Built with **.NET MAUI** and strict **Senior-level MVVM architectures**, this tool doesn't just read your database—it audits it like a Senior Database Administrator. By leveraging **Hybrid AI (Google Gemini Cloud & Ollama Local)**, we transform complex metadata into strategic roadmaps, ensuring your data layer is scalable, secure, and highly performant before a single line of application code goes to production.

## Key Features
* **Deep Metadata Extraction:** Native support for SQL Server, PostgreSQL, and MySQL.
* **Hybrid AI Support:** Toggle instantly between local, privacy-first analysis (Ollama) and high-scale cloud intelligence (Gemini).
* **Interactive ER Visualization:** Live, zoomable, and clickable ER diagrams powered by Mermaid.js and SVG-Pan-Zoom.
* **Professional PDF Reporting:** Export comprehensive, executive-level analysis reports via QuestPDF.
* **Secure & Clean Architecture:** 100% MVVM, Dependency Injection, and industry-standard protection for API keys via SecureStorage.

## Architectural Intelligence Suite
The analyzer comes equipped with 30+ specialized prompts that audit your database across critical pillars:

### Performance & Indexing
* **Non-SARGable Query Predicates & Missing Indexes:** Detects hidden query performance traps and high-cardinality columns lacking B-Tree indexes.
* **Over-indexing & Overlapping Costs:** Identifies redundant composite indexes that bloat storage and slow down DML operations.
* **Trigger-Induced Bottlenecks:** Analyzes recursive loops and deadlock risks caused by triggers executing during concurrent updates.
* **Execution Plan Simulation:** Mentally simulates execution plans (Hash Match, Nested Loops) for critical queries to predict bottlenecks.
* **Foreign Key Index Deficiency:** Reports unindexed foreign keys that cause expensive, hidden full table scans during cascading deletes.
* **Heap Table & Primary Key Analysis:** Identifies tables lacking Clustered Indexes and explains physical fragmentation risks.

### Schema Design & Data Modeling
* **Bloated Records & Data Type Wastage:** Compares column types against random data samples to find storage waste (e.g., 255-chars for booleans).
* **Normalization Violations (3NF):** Detects transitive dependencies and repeating data groups within rows.
* **Poorly Designed Denormalization:** Audits duplicated data points for synchronization risks and lack of consistency guarantees.
* **Orphan Records Detection:** Finds missing logical relationships that lack physical foreign key constraints.
* **Circular Dependency Mapping:** Uses graph-based routing to detect infinite loops in database architecture.
* **Hidden Complexity in Views:** Audits excessive nesting and suggests Common Table Expressions (CTEs) or materialized views.
* **Recursive CTE Misusage:** Investigates the absence of depth constraints to prevent recursive depth overflows.
* **Siloed Schema Modules:** Groups tables into business domains (DDD) and identifies non-integrated database silos.
* **Data Type Inconsistency:** Finds identical columns across tables with conflicting data types (implicit conversion risks).
* **Table Width & Page Fragmentation:** Flags wide tables that risk exceeding physical 8KB page limits (Row-chaining).
* **Identity Column Capacity Audit:** Warns against capacity exhaustion when using `TINYINT` or `SMALLINT` for highly active primary keys.
* **Naming Standards:** Reviews CamelCase/snake_case consistency across tables and columns.
* **Automated Data Dictionary:** Generates Markdown documentation explaining the business purpose of each table.

### Security, Privacy & Integrity
* **RBAC & Least Privilege Violations:** Audits permission tables to detect erroneous direct access to critical system entities.
* **Sensitive Data (PII/PHI) Encryption:** Uses NER logic to find unmasked identification, health, or financial records lacking encryption at rest.
* **Over-reliance on Client-Side Controls:** Identifies missing `CHECK` constraints, highlighting business logic vulnerabilities.
* **Privilege Escalation (IDOR):** Assesses the risk of using predictable sequential integers (auto-increment) instead of UUIDs for external services.
* **Dynamic Procedures & SQL Injection:** Inspects stored procedures for unsanitized inputs and Second-Order SQLi risks.
* **Double Spending & Race Conditions:** Audits wallet/inventory tables for the absence of Optimistic or Pessimistic locking columns (versioning).
* **Missing Audit Trails:** Detects sensitive tables lacking CDC or auditing columns (`created_at`, `updated_by`).
* **GDPR/HIPAA Data Retention:** Checks for the absence of mechanisms (Time-to-Live, soft deletes) dictating when data must be anonymized.
* **Format Bypass (Data Poisoning):** Locates fields (email, IP) lacking Regex/Domain validation constraints.
* **AI Model Collapse Detection:** Scans sample data for synthetic pollution generated by LLMs to verify model training suitability.
* **Polymorphic Relationship Flaws:** Analyzes dual-column combinations (`entity_id` / `entity_type`) for referential integrity loss.
* **Soft Delete Consistency:** Highlights reporting inaccuracies caused by non-uniform deletion logic (`IsDeleted`, `StatusID`).
* **Excessive Nullability:** Audits tables where business-critical columns allow `NULL` without corresponding constraints.

### 🚀 Scalability & Architecture
* **Horizontal Scalability Bottlenecks:** Evaluates foreign key bindings to identify tables suitable for partition keys in a microservices transition.
* **Improper Time-Series Storage:** Explains why storing high-frequency logs/metrics in relational B-Tree schemas cripples write performance.
* **ORM Anti-Patterns:** Detects fragmented designs prone to generating N+1 query problems in backend integrations.
* **Collation Mismatches:** Detects character set differences between joined tables that invalidate indexes.
* **Connection Pool Exhaustion:** Analyzes metadata, sessions, and temp table logic for flaws that bloat connection pools.
* **Platform Migration Risk:** Identifies proprietary engine-specific dependencies complicating moves to open-source or cloud-native DBs.
* **Microservice Boundary Grouping:** Suggests logical data boundaries to decompose monolithic schemas.

---

<br>

# 🇹🇷 Türkçe Versiyon

## Vizyon
Yazılım geliştirme dünyasında, veritabanı şemaları genellikle performans darboğazlarının ve güvenlik açıklarının gizlendiği "kara kutulara" dönüşür. **AI Database Analyzer**, ham veritabanı şemaları ile uzman düzeyindeki optimizasyon görüşleri arasındaki boşluğu doldurmak için tasarlanmış kurumsal düzeyde bir mimari zeka çözümüdür.

**.NET MAUI** ile geliştirilen ve katı **Senior seviye MVVM mimarisi** ile kurgulanan bu araç, cebinizdeki otonom **Kıdemli Veritabanı Yöneticisi (DBA)** olarak görev yapar. **Hibrit Yapay Zeka (Gemini Bulut ve Ollama Yerel)** gücünü kullanarak, karmaşık metadatayı aksiyon alınabilir stratejik yol haritalarına dönüştürüyoruz.

## Temel Özellikler
* **Derin Metadata Çıkarımı:** SQL Server, PostgreSQL ve MySQL için yerel destek.
* **Hibrit Yapay Zeka:** Yerel gizlilik odaklı analiz (Ollama) ile yüksek ölçekli bulut zekası (Gemini) arasında anlık geçiş.
* **İnteraktif ER Görselleştirme:** Mermaid.js altyapılı, yakınlaştırılabilir ve tıklanabilir (veri önizlemeli) canlı diyagramlar.
* **Kurumsal Raporlama:** QuestPDF üzerinden yönetici özetli profesyonel PDF analiz raporları çıktısı.
* **Temiz Mimari:** %100 MVVM, Dependency Injection ve hassas API anahtarları için SecureStorage ile uçtan uca güvenlik.

## Mimari Zeka Paketi (Analiz Konuları)
Analizör, veritabanınızı 30'dan fazla uzman prompt (komut) ile denetler:

### Performans ve İndeksleme
* **SARGable Olmayan Sorgular ve Eksik İndeksler:** İndeks kullanımını engelleyen tasarımları ve yüksek kardinaliteli eksik indeksleri bulur.
* **Aşırı İndeksleme ve Çakışan Maliyetler:** Depolamayı şişiren ve yazma performansını düşüren gereksiz kompozit indeksleri belirler.
* **Tetikleyici (Trigger) Darboğazları:** DML işlemlerindeki döngüsel kilitlenmeleri (deadlock) ve performans kayıplarını analiz eder.
* **Sorgu Yürütme Planı Simülasyonu:** Örnek verilerle Table Scan, Hash Match risklerini tahmin edip sorgu optimizasyonu önerir.
* **Dış Anahtar (FK) İndeks Eksikliği:** Silme işlemlerinde gizli tam tablo taramasına yol açan indekslenmemiş FK'ları raporlar.
* **Heap Tablo ve PK Analizi:** Primary Key'i olmayan dağınık tabloları bulur ve fragmantasyon risklerini açıklar.

### Şema Tasarımı ve Modelleme
* **Şişirilmiş Kayıtlar ve Veri Türü İsrafı:** Sütun tiplerini örnek verilerle (örn. boolean için VARCHAR(255)) karşılaştırarak israfı bulur.
* **Normalizasyon İhlalleri (3NF):** Geçişli bağımlılıkları ve veri tekrarlarını yapısal olarak analiz eder.
* **Kötü Tasarlanmış Denormalizasyon:** Senkronizasyon garantisi olmayan kopyalanmış verilerdeki tutarsızlık risklerini bulur.
* **Yetim Kayıtlar (Orphan Records):** Mantıksal bağı olan ancak fiziksel (FK) kısıtlaması unutulmuş tabloları eşleştirir.
* **Dairesel Bağımlılık (Circular Dependency):** İlişkiler ve tetikleyiciler arasındaki sonsuz döngü rotalarını çizer.
* **Görünüm (View) Karmaşıklığı:** Aşırı iç içe geçmiş View'ları bulup CTE veya Materialized View önerisi sunar.
* **Recursive CTE Hataları:** Hiyerarşik tablolarda derinlik sınırı olmayan (sonsuz döngü riski) yapıları inceler.
* **Kopuk Şema Modülleri (Silos):** Tabloları iş mantığına (DDD) göre gruplayıp izole kalmış tabloları bulur.
* **Veri Tipi Uyuşmazlıkları:** Farklı tablolardaki aynı isimli kolonların (örn. `user_id`) tip farklılıklarını (Implicit Conversion riski) tespit eder.
* **Tablo Genişliği Riski:** Fiziksel 8KB sayfa sınırını aşarak Row-Chaining yaratabilecek aşırı geniş tabloları uyarır.
* **Identity Kolon Kapasitesi:** Çok aktif tablolarda `TINYINT` gibi küçük tiplerle oluşturulmuş PK'ların tükenme riskini hesaplar.
* **İsimlendirme Standartları:** CamelCase / snake_case tutarsızlıklarını denetler.
* **Otomatik Veri Sözlüğü:** Tabloların ve kolonların iş amaçlarını açıklayan Markdown tabanlı dökümantasyon üretir.

### Güvenlik, Gizlilik ve Bütünlük
* **RBAC ve "En Az Ayrıcalık" İhlalleri:** Rol tabanlı erişim tablolarındaki hatalı doğrudan yetkilendirmeleri bulur.
* **Hassas Veri (PII/PHI) Şifreleme Eksikliği:** Maskelenmemiş kimlik, sağlık veya kredi kartı verilerini tespit edip şifreleme önerir.
* **İstemci Tarafına Aşırı Güven:** E-ticaret gibi akışlarda eksik `CHECK` kısıtlamalarından doğan (örn. negatif bakiye) Business Logic açıklarını arar.
* **Ayrıcalık Yükseltme (IDOR):** Dışarıya açık tablolarda UUID yerine tahmin edilebilir sıralı ID (auto-increment) kullanım risklerini belirler.
* **Dinamik Prosedürler (SQL Enjeksiyonu):** Stored Procedure'lerde sanitize edilmemiş girdileri ve Second-Order SQLi zafiyetlerini arar.
* **Çift Harcama (Race Condition):** Cüzdan/stok tablolarında "Optimistic/Pessimistic Locking" (versiyonlama) eksikliklerini denetler.
* **Eksik Denetim İzi (Audit Trail):** Hassas tablolarda değişikliği takip eden `updated_by`, `created_at` gibi CDC kolonlarının yokluğunu raporlar.
* **GDPR/HIPAA Veri Saklama İhlali:** Log ve işlem tablolarında verinin ne zaman anonimleşeceğini (TTL) belirleyen mekanizmaların eksikliğini arar.
* **Girdi Manipülasyonu (Data Poisoning):** E-posta, IP gibi alanlarda Regex/Domain kısıtlaması eksikliklerini bulur.
* **Model Çöküşü (AI Sentetik Kirlilik):** Şemadaki örnek verilerin LLM'ler tarafından sentetik üretilip üretilmediğini tespit eder.
* **Polimorfik İlişki Hataları:** `entity_id` / `entity_type` yapılarının FK kısıtlaması olmamasından doğan referans bütünlüğü kaybını inceler.
* **Soft Delete Tutarsızlığı:** Sistem genelinde `IsDeleted` veya `StatusID` kullanımının standart olup olmadığını denetler.
* **Aşırı Nullability:** Kritik iş kolonlarında kısıtlama olmadan `NULL` değerlere izin verilme oranını (kirli veri riski) analiz eder.

### Ölçeklenebilirlik ve Modernizasyon
* **Yatay Ölçekleme Darboğazları:** Yüksek FK yoğunluğuna sahip tabloların mikroservislere ayrılma zorluklarını analiz eder.
* **Zaman Serisi (Time-Series) Mimari Hataları:** Yüksek frekanslı log/metrik tablolarında klasik B-Tree kullanımının yazma performansını nasıl çökerteceğini açıklar.
* **ORM Anti-Pattern'leri (N+1):** Sık beraber sorgulanan ama mantıksızca parçalanmış, N+1 sorununa yatkın tablo tasarımlarını bulur.
* **Collation (Karakter Seti) Uyuşmazlığı:** JOIN işlemlerinde indeksi iptal eden Collation farklılıklarını raporlar.
* **Bağlantı Havuzu (Connection Pool) Aşımı:** Tetikleyiciler içindeki hatalı session veya temp table mantıklarını tespit eder.
* **Platform Taşıma (Migration) Riski:** SQL Server'dan PostgreSQL'e geçerken baş ağrıtacak motora özel (proprietary) bağımlılıkları listeler.
* **Mikroservis Sınır Gruplandırması (DDD):** Monolitik veritabanını mikroservislere bölmek için mantıksal sınırlar (Bounded Contexts) çizer.

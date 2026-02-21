# 🔥 Integration Platform - Enterprise ETL & Workflow Otomasyonu

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB?style=flat-square&logo=react)](https://reactjs.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-4169E1?style=flat-square&logo=postgresql)](https://www.postgresql.org/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

## 📋 Proje Hakkında

**Integration Platform**, kurumsal sistemler arasında veri entegrasyonunu sağlayan, **modüler**, **ölçeklenebilir** ve **kullanıcı dostu** bir ETL (Extract, Transform, Load) ve iş akışı otomasyon platformudur.

### 🎯 Neden Integration Platform?

- 🔌 **Plugin Mimarisi**: İhtiyacınız olan her adapter'ı sonradan ekleyin
- 🎨 **Görsel Tasarımcı**: Sürükle-bırak ile workflow oluşturun
- ⚡ **Dağıtık Çalışma**: Worker'ları istediğiniz kadar çoğaltın
- 🔒 **Güvenli**: JWT authentication, role-based authorization
- 📊 **Gerçek Zamanlı İzleme**: Tüm süreçleri anlık takip edin

## 🏗️ Mimari Yapı

```
IntegrationPlatform/
├── 📁 IntegrationPlatform.Common/          # Ortak DTO'lar, Interface'ler, Enum'lar
├── 📁 IntegrationPlatform.API/              # REST API (Node/Workflow yönetimi)
├── 📁 IntegrationPlatform.Worker/           # Dağıtık işçi servisler
├── 📁 IntegrationPlatform.Dashboard/        # React + TypeScript UI
└── 📁 IntegrationPlatform.Adapters/         # Plugin'ler
    ├── 📁 RestAdapter/                      # REST API Source
    ├── 📁 JsonAdapter/                       # JSON Reader/Writer/Transform
    ├── 📁 DatabaseAdapter/                    # SQL Source/Destination
    └── 📁 [Yeni Adapter'lar]...              # FTP, Mail, Queue, etc.
```

## ✨ Özellikler

### 🎨 **Görsel Workflow Tasarımcısı**
- Sürükle-bırak ile kolay workflow oluşturma
- 15+ hazır adapter (REST, Database, JSON, FTP, Excel, CSV, XML)
- Gerçek zamanlı bağlantı testi
- Alan eşleme (mapping) arayüzü

### 🔌 **Plugin Sistemi**
- DLL tabanlı hot-plug mimari
- Her adapter bağımsız versiyonlanabilir
- Çalışırken yeni adapter ekleme
- İzole çalışma alanı (AssemblyLoadContext)

### ⚙️ **Worker Daemon**
- Dağıtık çalışma mimarisi
- Otomatik node keşfi ve kayıt
- Load balancing
- Heartbeat ile sağlık kontrolü
- Otomatik yeniden başlatma

### 📊 **Dashboard**
- Node ve workflow durumları
- Canlı log izleme
- Performans metrikleri
- Grafiksel raporlama

### 🗄️ **Desteklenen Veri Kaynakları**
- REST/SOAP API'ler
- SQL Server, PostgreSQL, MySQL, Oracle
- JSON, XML, CSV, Excel dosyaları
- FTP/SFTP sunucuları
- MongoDB (yakında)
- RabbitMQ/Kafka (yakında)

## 🚀 Hızlı Başlangıç

### Gereksinimler
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- [PostgreSQL 15+](https://www.postgresql.org/)
- [Docker](https://www.docker.com/) (opsiyonel)

### Adım 1: Repository'yi klonlayın
```bash
git clone https://github.com/yourusername/IntegrationPlatform.git
cd IntegrationPlatform
```

### Adım 2: Veritabanını oluşturun
```bash
cd IntegrationPlatform.API
dotnet ef database update
```

### Adım 3: API'yi çalıştırın
```bash
dotnet run --project IntegrationPlatform.API
```

### Adım 4: Worker'ları başlatın
```bash
dotnet run --project IntegrationPlatform.Worker
# İkinci worker için yeni terminal:
dotnet run --project IntegrationPlatform.Worker
```

### Adım 5: Dashboard'u başlatın
```bash
cd IntegrationPlatform.Dashboard
npm install
npm start
```

### Adım 6: Tarayıcıdan açın
```
http://localhost:3000
```

## 🐳 Docker ile Çalıştırma

```bash
# Tüm servisleri ayağa kaldır
docker-compose up -d

# Sadece veritabanı
docker-compose up -d postgres pgadmin seq

# Logları izleme
docker-compose logs -f
```

## 📦 Adapter Geliştirme

Yeni bir adapter eklemek için:

```csharp
public class MyNewAdapter : ISourcePlugin
{
    public string Id => "IntegrationPlatform.Adapters.MyNewAdapter";
    public string Name => "My New Source";
    public AdapterType Type => AdapterType.MyNewSource;
    public AdapterDirection Direction => AdapterDirection.Source;

    public async Task<SourceData> FetchAsync(SourceContext context)
    {
        // Veri çekme mantığı
    }

    public async Task<SourceTestResult> TestConnectionAsync(Dictionary<string, object> config)
    {
        // Bağlantı testi
    }
}
```

## 📸 Ekran Görüntüleri

*(Buraya proje ekran görüntüleri eklenecek)*

## 🛣️ Yol Haritası

- **v1.0.0** ✅ Temel ETL işlevleri (REST, JSON, Database)
- **v1.1.0** 🔄 FTP, Mail, Queue adapter'ları
- **v1.2.0** 📊 Gelişmiş monitoring (Prometheus + Grafana)
- **v2.0.0** 🚀 Cluster mode, High Availability
- **v2.1.0** 🤖 AI-powered mapping önerileri

## 🤝 Katkıda Bulunma

1. Fork'layın (https://github.com/XianThi/IntegrationPlatform/fork)
2. Feature branch oluşturun (`git checkout -b feature/amazing-feature`)
3. Değişikliklerinizi commit edin (`git commit -m 'Add amazing feature'`)
4. Branch'inizi push edin (`git push origin feature/amazing-feature`)
5. Pull Request açın

## 📝 Lisans

Bu proje MIT lisansı ile lisanslanmıştır. Detaylar için [LICENSE](LICENSE) dosyasına bakın.

## 📧 İletişim

Proje Sahibi - [@XianThi](https://twitter.com/XianThi) 

Proje Linki: [https://github.com/XianThi/IntegrationPlatform](https://github.com/XianThi/IntegrationPlatform)

## ⭐ Teşekkürler

- [React Flow](https://reactflow.dev/) - Workflow tasarımcısı için
- [Material-UI](https://mui.com/) - Dashboard UI için
- [Npgsql](https://www.npgsql.org/) - PostgreSQL driver
- Tüm katkıda bulunanlar ❤️

---

**⭐ Star'lamayı unutmayın!**
-- Sanitary POS — SQLite Database Schema v1
-- Bilingual Urdu/English, Pakistan market, offline desktop (.NET 8 WPF)
-- Money: DECIMAL stored as REAL is risky -> store money as INTEGER PAISA (Rs 1 = 100).
--        Display layer converts paisa -> "Rs 1,25,000". Dates: TEXT ISO 'yyyy-MM-dd HH:mm:ss'.
PRAGMA foreign_keys = ON;

-- ========= USERS & SETTINGS =========
CREATE TABLE users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,              -- never store plain password/PIN
  full_name TEXT NOT NULL,
  role TEXT NOT NULL CHECK(role IN ('Admin','Cashier')),
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value TEXT
);
-- keys used: shop_name_en, shop_name_ur, shop_address, shop_phone, shop_whatsapp,
-- ntn, strn, tax_enabled (0/1), tax_rate_percent, default_language ('en'/'ur'),
-- receipt_printer, a4_printer, backup_folder, quotation_valid_days (7)

-- ========= CATALOG =========
CREATE TABLE brands (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL UNIQUE,                -- Master, Faisal, Porta, Steelline ...
  is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name_en TEXT NOT NULL UNIQUE,             -- Taps, Wash Basin, Commode ...
  name_ur TEXT,                             -- نلکے etc. (display only)
  is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE units (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name_en TEXT NOT NULL UNIQUE,             -- Piece, Set, Pair, Box, Meter, Dozen
  name_ur TEXT
);

CREATE TABLE products (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,                       -- stays English in both languages
  brand_id INTEGER REFERENCES brands(id),
  category_id INTEGER REFERENCES categories(id),
  model_code TEXT,
  barcode TEXT UNIQUE,
  finish TEXT,                              -- Chrome, Matte Black, Gold, White
  size TEXT,                                -- 1/2", 3/4", etc.
  unit_id INTEGER REFERENCES units(id),
  purchase_rate_paisa INTEGER NOT NULL DEFAULT 0,   -- Admin only
  retail_rate_paisa   INTEGER NOT NULL DEFAULT 0,
  trade_rate_paisa    INTEGER NOT NULL DEFAULT 0,   -- plumber / contractor
  wholesale_rate_paisa INTEGER NOT NULL DEFAULT 0,
  low_stock_level REAL NOT NULL DEFAULT 2,
  location TEXT NOT NULL DEFAULT 'Shop' CHECK(location IN ('Shop','Godown')),
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX idx_products_name ON products(name);
CREATE INDEX idx_products_barcode ON products(barcode);

-- Current stock per product (updated ONLY through stock_movements triggers/app logic)
CREATE TABLE stock (
  product_id INTEGER PRIMARY KEY REFERENCES products(id),
  qty REAL NOT NULL DEFAULT 0,              -- REAL because Meter/weight items can be fractional
  damaged_qty REAL NOT NULL DEFAULT 0
);

CREATE TABLE stock_movements (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  product_id INTEGER NOT NULL REFERENCES products(id),
  movement_type TEXT NOT NULL CHECK(movement_type IN
    ('Purchase','Sale','SaleReturn','PurchaseReturn','Damage','Adjustment','Opening')),
  qty REAL NOT NULL,                        -- + in, - out
  reference_type TEXT,                      -- 'Sale','Purchase','Return'
  reference_id INTEGER,
  note TEXT,
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX idx_movements_product ON stock_movements(product_id);

-- ========= PEOPLE =========
CREATE TABLE customers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  mobile TEXT,                              -- 03XX-XXXXXXX
  address TEXT,
  city TEXT,
  customer_type TEXT NOT NULL DEFAULT 'Regular'
    CHECK(customer_type IN ('WalkIn','Regular','Plumber','Contractor')),
  credit_limit_paisa INTEGER NOT NULL DEFAULT 0,   -- 0 = no limit set
  opening_balance_paisa INTEGER NOT NULL DEFAULT 0, -- old udhaar when starting the software
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX idx_customers_mobile ON customers(mobile);
CREATE INDEX idx_customers_name ON customers(name);

CREATE TABLE suppliers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  contact_person TEXT,
  mobile TEXT,
  address TEXT,
  city TEXT,
  opening_balance_paisa INTEGER NOT NULL DEFAULT 0, -- amount we already owed them
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

-- One payments table for both sides: IN = customer paid us (Wasooli), OUT = we paid supplier
CREATE TABLE payments (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  party_type TEXT NOT NULL CHECK(party_type IN ('Customer','Supplier')),
  party_id INTEGER NOT NULL,                -- customers.id or suppliers.id per party_type
  direction TEXT NOT NULL CHECK(direction IN ('IN','OUT')),
  amount_paisa INTEGER NOT NULL,
  payment_method TEXT NOT NULL DEFAULT 'Cash'
    CHECK(payment_method IN ('Cash','Card','Bank','JazzCash','EasyPaisa')),
  sale_id INTEGER REFERENCES sales(id),     -- set when paid against a specific bill
  note TEXT,
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX idx_payments_party ON payments(party_type, party_id);

-- ========= SALES =========
CREATE TABLE sales (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  bill_no INTEGER NOT NULL UNIQUE,          -- printed bill number, sequential
  customer_id INTEGER REFERENCES customers(id),  -- NULL = walk-in cash customer
  price_level TEXT NOT NULL DEFAULT 'Retail'
    CHECK(price_level IN ('Retail','Trade','Wholesale')),
  subtotal_paisa INTEGER NOT NULL DEFAULT 0,
  item_discount_paisa INTEGER NOT NULL DEFAULT 0,
  bill_discount_paisa INTEGER NOT NULL DEFAULT 0,
  tax_paisa INTEGER NOT NULL DEFAULT 0,     -- FBR-ready: tax kept separate on every bill
  total_paisa INTEGER NOT NULL DEFAULT 0,
  paid_paisa INTEGER NOT NULL DEFAULT 0,    -- paid now; total-paid = udhaar added to khata
  payment_method TEXT NOT NULL DEFAULT 'Cash'
    CHECK(payment_method IN ('Cash','Card','Bank','JazzCash','EasyPaisa','Credit','Mixed')),
  status TEXT NOT NULL DEFAULT 'Completed'
    CHECK(status IN ('Completed','Held','Cancelled')),
  cancel_reason TEXT,
  quotation_id INTEGER REFERENCES quotations(id), -- set if converted from a quotation
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX idx_sales_date ON sales(created_at);
CREATE INDEX idx_sales_customer ON sales(customer_id);

CREATE TABLE sale_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sale_id INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
  product_id INTEGER NOT NULL REFERENCES products(id),
  product_name_snapshot TEXT NOT NULL,      -- name/rate at time of sale, never changes later
  qty REAL NOT NULL,
  rate_paisa INTEGER NOT NULL,
  discount_paisa INTEGER NOT NULL DEFAULT 0,
  line_total_paisa INTEGER NOT NULL
);

-- ========= QUOTATIONS =========
CREATE TABLE quotations (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  quotation_no INTEGER NOT NULL UNIQUE,
  customer_id INTEGER REFERENCES customers(id),
  customer_name_snapshot TEXT,              -- for house customers not saved as customers
  customer_mobile_snapshot TEXT,
  price_level TEXT NOT NULL DEFAULT 'Retail'
    CHECK(price_level IN ('Retail','Trade','Wholesale')),
  subtotal_paisa INTEGER NOT NULL DEFAULT 0,
  discount_paisa INTEGER NOT NULL DEFAULT 0,
  total_paisa INTEGER NOT NULL DEFAULT 0,
  valid_days INTEGER NOT NULL DEFAULT 7,    -- "Rates valid for 7 days"
  status TEXT NOT NULL DEFAULT 'Open'
    CHECK(status IN ('Open','Converted','Expired','Cancelled')),
  converted_sale_id INTEGER REFERENCES sales(id),
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE quotation_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  quotation_id INTEGER NOT NULL REFERENCES quotations(id) ON DELETE CASCADE,
  product_id INTEGER NOT NULL REFERENCES products(id),
  product_name_snapshot TEXT NOT NULL,
  qty REAL NOT NULL,
  rate_paisa INTEGER NOT NULL,
  discount_paisa INTEGER NOT NULL DEFAULT 0,
  line_total_paisa INTEGER NOT NULL
);

-- ========= PURCHASES (Phase 2, table ready now) =========
CREATE TABLE purchases (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
  supplier_bill_no TEXT,
  subtotal_paisa INTEGER NOT NULL DEFAULT 0,
  discount_paisa INTEGER NOT NULL DEFAULT 0,
  total_paisa INTEGER NOT NULL DEFAULT 0,
  paid_paisa INTEGER NOT NULL DEFAULT 0,
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE purchase_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  purchase_id INTEGER NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
  product_id INTEGER NOT NULL REFERENCES products(id),
  qty REAL NOT NULL,
  purchase_rate_paisa INTEGER NOT NULL,
  line_total_paisa INTEGER NOT NULL
);

-- ========= RETURNS (Phase 2, tables ready now) =========
CREATE TABLE returns (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sale_id INTEGER NOT NULL REFERENCES sales(id),
  customer_id INTEGER REFERENCES customers(id),
  total_refund_paisa INTEGER NOT NULL DEFAULT 0,
  settle_method TEXT NOT NULL DEFAULT 'Cash' CHECK(settle_method IN ('Cash','Khata','Exchange')),
  user_id INTEGER REFERENCES users(id),
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE return_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  return_id INTEGER NOT NULL REFERENCES returns(id) ON DELETE CASCADE,
  product_id INTEGER NOT NULL REFERENCES products(id),
  qty REAL NOT NULL,
  rate_paisa INTEGER NOT NULL,
  is_damaged INTEGER NOT NULL DEFAULT 0,    -- 1 -> goes to damaged_qty, not sellable stock
  line_total_paisa INTEGER NOT NULL
);

-- ========= SEED DATA =========
INSERT INTO units (name_en, name_ur) VALUES
 ('Piece','عدد'),('Set','سیٹ'),('Pair','جوڑا'),('Box','ڈبہ'),('Meter','میٹر'),('Dozen','درجن');

INSERT INTO categories (name_en, name_ur) VALUES
 ('Taps','نلکے'),('Mixers','مکسر'),('Wash Basin','واش بیسن'),('Kitchen Sink','کچن سنک'),
 ('Shower','شاور'),('Commode / WC','کموڈ / ڈبلیو سی'),('Flush Tank','فلش ٹینک'),
 ('Pipes','پائپ'),('Valves','وال'),('Bathroom Accessories','باتھ روم لوازمات'),('Fittings','فٹنگ');

INSERT INTO settings (key, value) VALUES
 ('shop_name_en','My Sanitary Store'),('shop_name_ur','میری سینیٹری اسٹور'),
 ('shop_address',''),('shop_phone',''),('shop_whatsapp',''),
 ('tax_enabled','0'),('tax_rate_percent','0'),
 ('default_language','ur'),('quotation_valid_days','7');

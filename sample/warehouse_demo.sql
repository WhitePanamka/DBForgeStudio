CREATE DATABASE IF NOT EXISTS dbforge_warehouse_demo CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE dbforge_warehouse_demo;
CREATE TABLE IF NOT EXISTS categories (
  id INT NOT NULL AUTO_INCREMENT,
  name VARCHAR(120) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_categories_name (name)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS products (
  id INT NOT NULL AUTO_INCREMENT,
  category_id INT NOT NULL,
  name VARCHAR(180) NOT NULL,
  sku VARCHAR(60) NOT NULL,
  quantity INT NOT NULL DEFAULT 0,
  unit_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_products_sku (sku),
  CONSTRAINT fk_products_categories FOREIGN KEY (category_id) REFERENCES categories(id),
  CONSTRAINT chk_products_quantity CHECK (quantity >= 0),
  CONSTRAINT chk_products_price CHECK (unit_price >= 0)
) ENGINE=InnoDB;
INSERT IGNORE INTO categories(name) VALUES ('Канцелярия'), ('Комплектующие');
INSERT IGNORE INTO products(category_id, name, sku, quantity, unit_price)
SELECT c.id, 'Блокнот A5', 'NOTE-A5-001', 40, 129.90 FROM categories c WHERE c.name='Канцелярия';
INSERT IGNORE INTO products(category_id, name, sku, quantity, unit_price)
SELECT c.id, 'USB-клавиатура', 'KEY-USB-001', 12, 799.00 FROM categories c WHERE c.name='Комплектующие';

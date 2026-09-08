/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data.Repositories
{
    /// <summary>
    /// Generic repository over a plain EF Core <see cref="DbContext"/> (SQLite).
    ///
    /// Methods without the "Async" suffix are DEFERRED: they enqueue a command onto
    /// <see cref="IDbContext"/> and are flushed atomically by IUnitOfWork.Commit().
    /// The "*Async" variants execute immediately, like the old Mongo repository.
    ///
    /// Migration notes (vs the previous MongoDB.Driver version):
    ///  - the public <c>IMongoCollection&lt;T&gt; Collection</c> property is GONE;
    ///    call sites that used it directly are ported individually.
    ///  - id-keyed helpers take <see cref="string"/> instead of <c>ObjectId</c>
    ///    and require the entity to have a single scalar primary key.
    ///  - <see cref="Update(TEntity)"/> replaces <c>Update(ObjectId, TEntity)</c>
    ///    (the entity already carries its key).
    /// </summary>
    public class Repository<TEntity> where TEntity : class
    {
        protected readonly IDbContext dbContext;
        protected readonly DbContext db;

        public Repository(IDbContext dbContext, DbContext db)
        {
            this.dbContext = dbContext;
            this.db = db;
        }

        protected DbSet<TEntity> Set => db.Set<TEntity>();

        // ---------------- deferred writes (buffered on the unit of work) ----------------

        public virtual void Insert(TEntity obj) =>
            dbContext.AddCommand(_ => { Set.Add(obj); return Task.CompletedTask; });

        public virtual void Upsert(TEntity obj, Expression<Func<TEntity, bool>> filterExpression) =>
            dbContext.AddCommand(async ct =>
            {
                var existing = await Set.FirstOrDefaultAsync(filterExpression, ct);
                if (existing is null) Set.Add(obj);
                else db.Entry(existing).CurrentValues.SetValues(obj);
            });

        public virtual void Update(TEntity obj) =>
            dbContext.AddCommand(_ => { Set.Update(obj); return Task.CompletedTask; });

        public virtual void Delete(Expression<Func<TEntity, bool>> filterExpression) =>
            dbContext.AddCommand(ct => Set.Where(filterExpression).ExecuteDeleteAsync(ct));

        public virtual void DeleteMany(Expression<Func<TEntity, bool>> filterExpression) =>
            dbContext.AddCommand(ct => Set.Where(filterExpression).ExecuteDeleteAsync(ct));

        public virtual void DeleteById(string id) =>
            dbContext.AddCommand(async ct =>
            {
                var existing = await Set.FindAsync([id], ct);
                if (existing is not null) Set.Remove(existing);
            });

        // ---------------- immediate reads ----------------

        public virtual Task<TEntity?> FindOneAsync(Expression<Func<TEntity, bool>> filterExpression) =>
            Set.AsNoTracking().FirstOrDefaultAsync(filterExpression);

        public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> filterExpression) =>
            await Set.AsNoTracking().Where(filterExpression).ToListAsync();

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync() =>
            await Set.AsNoTracking().ToListAsync();

        public virtual async Task<TEntity?> GetAsync(string id) =>
            await Set.FindAsync(id);

        public virtual Task<long> CountAsync(Expression<Func<TEntity, bool>> filterExpression) =>
            Set.LongCountAsync(filterExpression);

        // ---------------- immediate writes ----------------

        public virtual async Task InsertAsync(TEntity obj)
        {
            Set.Add(obj);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public virtual async Task UpsertAsync(TEntity obj, Expression<Func<TEntity, bool>> filterExpression)
        {
            var existing = await Set.FirstOrDefaultAsync(filterExpression);
            if (existing is null) Set.Add(obj);
            else db.Entry(existing).CurrentValues.SetValues(obj);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public virtual async Task UpdateAsync(TEntity obj)
        {
            Set.Update(obj);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public virtual Task DeleteAsync(Expression<Func<TEntity, bool>> filterExpression) =>
            Set.Where(filterExpression).ExecuteDeleteAsync();

        public virtual Task DeleteManyAsync(Expression<Func<TEntity, bool>> filterExpression) =>
            Set.Where(filterExpression).ExecuteDeleteAsync();

        public virtual async Task DeleteByIdAsync(string id)
        {
            var existing = await Set.FindAsync(id);
            if (existing is null) return;
            Set.Remove(existing);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }
}

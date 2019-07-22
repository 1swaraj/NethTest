/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using FluentAssertions;
using Nethermind.Core.Crypto;
using Nethermind.DataMarketplace.Core;
using Nethermind.DataMarketplace.Core.Domain;
using Nethermind.DataMarketplace.Core.Services;
using NUnit.Framework;

namespace Nethermind.DataMarketplace.Test.Services
{
    public class NdmDataPublisherTests
    {
        private INdmDataPublisher _dataPublisher;

        [SetUp]
        public void Setup()
        {
            _dataPublisher = new NdmDataPublisher();
        }

        [Test]
        public void publish_should_invoke_data_published_event()
        {
            object sender = null;
            NdmDataEventArgs eventArgs = null;
            var headerId = Keccak.Zero;
            var headerData = new Dictionary<string, string>();
            var data = new DataHeaderData(headerId, headerData);
            _dataPublisher.DataPublished += (s, e) =>
            {
                sender = s;
                eventArgs = e;
            };
            _dataPublisher.Publish(data);
            sender.Should().Be(_dataPublisher);
            eventArgs.DataHeaderData.Should().Be(data);
            data.Id.Should().Be(headerId);
            data.Data.Should().BeEquivalentTo(headerData);
        }
    }
}